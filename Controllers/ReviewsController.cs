using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PullSight.Api.Contracts.GitHub;
using PullSight.Api.Contracts.Reviews;
using PullSight.Api.Services.GitHub;
using PullSight.Api.Services.ReviewAnalysis;

namespace PullSight.Api.Controllers;

[ApiController]
[Route("api/reviews")]
public sealed class ReviewsController(
    RuleBasedCodeReviewAnalyzer demoAnalyzer,
    GitHubApiService gitHubApiService,
    ReviewAnalysisOrchestrator reviewAnalysisOrchestrator,
    ReviewHistoryService reviewHistoryService,
    ReviewComparisonService reviewComparisonService,
    ReviewReportService reviewReportService,
    ReviewPublishService reviewPublishService,
    ReviewGitHubPublishService reviewGitHubPublishService,
    ILogger<ReviewsController> logger) : ControllerBase
{
    [Authorize]
    [HttpPost("compare")]
    public async Task<ActionResult<ReviewComparisonResponse>> CompareReviews(
        CompareReviewsRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetGitHubUserId(out var githubUserId))
        {
            return Unauthorized();
        }

        var result = await reviewComparisonService.CompareAsync(
            githubUserId,
            request.BaseReviewRunId,
            request.TargetReviewRunId,
            cancellationToken);

        return result.Status switch
        {
            ReviewComparisonResultStatus.Success => Ok(result.Comparison),
            ReviewComparisonResultStatus.NotFound => Problem(
                title: "Review not found.",
                detail: result.ErrorMessage,
                statusCode: StatusCodes.Status404NotFound,
                extensions: new Dictionary<string, object?> { ["code"] = result.ErrorCode }),
            _ => Problem(
                title: "Reviews cannot be compared.",
                detail: result.ErrorMessage,
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = result.ErrorCode }),
        };
    }

    [Authorize]
    [HttpGet]
    public async Task<ActionResult<ReviewHistoryPageResponse>> GetHistory(
        [FromQuery] ReviewHistoryQuery request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetGitHubUserId(out var githubUserId))
        {
            return Unauthorized();
        }

        var result = await reviewHistoryService.GetReviewsAsync(
            githubUserId,
            request,
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Page)
            : Problem(
                title: "Invalid review history filters.",
                detail: result.ErrorMessage,
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = result.ErrorCode });
    }

    [Authorize]
    [HttpGet("{reviewRunId:guid}/export")]
    public async Task<IActionResult> ExportReview(
        Guid reviewRunId,
        [FromQuery] ReviewExportRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetGitHubUserId(out var githubUserId))
        {
            return Unauthorized();
        }

        var result = await reviewReportService.ExportReviewAsync(
            githubUserId,
            reviewRunId,
            request.Format,
            cancellationToken);

        return ToExportResult(result);
    }

    [Authorize]
    [HttpPost("compare/export")]
    public async Task<IActionResult> ExportComparison(
        ReviewComparisonExportRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetGitHubUserId(out var githubUserId))
        {
            return Unauthorized();
        }

        var result = await reviewReportService.ExportComparisonAsync(
            githubUserId,
            request,
            cancellationToken);

        return ToExportResult(result);
    }

    [Authorize]
    [HttpPost("{reviewRunId:guid}/check-run")]
    public async Task<ActionResult<CheckRunPublishResponse>> PublishCheckRun(
        Guid reviewRunId,
        CancellationToken cancellationToken)
    {
        if (!TryGetGitHubUserId(out var githubUserId))
        {
            return Unauthorized();
        }

        var accessToken = await HttpContext.GetTokenAsync("access_token") ?? string.Empty;
        var result = await reviewGitHubPublishService.PublishCheckRunAsync(
            githubUserId,
            reviewRunId,
            accessToken,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Response)
            : TypedGitHubPublishProblem(result.ErrorCode!, result.ErrorMessage!);
    }

    [Authorize]
    [HttpPost("{reviewRunId:guid}/inline-comments")]
    public async Task<ActionResult<InlineCommentsPublishResponse>> PublishInlineComments(
        Guid reviewRunId,
        InlineCommentsPublishRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetGitHubUserId(out var githubUserId))
        {
            return Unauthorized();
        }

        var accessToken = await HttpContext.GetTokenAsync("access_token") ?? string.Empty;
        var result = await reviewGitHubPublishService.PublishInlineCommentsAsync(
            githubUserId,
            reviewRunId,
            request,
            accessToken,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Response)
            : TypedGitHubPublishProblem(result.ErrorCode!, result.ErrorMessage!);
    }

    [Authorize]
    [HttpPost("{reviewRunId:guid}/retry")]
    public async Task<ActionResult<PullRequestReviewResponse>> RetryReview(
        Guid reviewRunId,
        CancellationToken cancellationToken)
    {
        if (!TryGetGitHubUserId(out var githubUserId))
        {
            return Unauthorized();
        }

        var review = await reviewHistoryService.GetReviewAsync(
            githubUserId,
            reviewRunId,
            cancellationToken);
        if (review is null)
        {
            return TypedProblem(
                "Review not found.",
                "The review run was not found.",
                StatusCodes.Status404NotFound,
                "review_not_found");
        }

        if (review.Status != "failed")
        {
            return TypedProblem(
                "Review cannot be retried.",
                "Only failed review runs can be retried.",
                StatusCodes.Status409Conflict,
                "review_not_failed");
        }

        var repository = review.RepositoryFullName.Split('/', 2);
        if (repository.Length != 2)
        {
            return TypedGitHubPublishProblem(
                "github_repository_unavailable",
                "The persisted repository is invalid.");
        }

        var accessToken = await HttpContext.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return TypedGitHubPublishProblem(
                "github_token_missing",
                "Log in with GitHub again before retrying this review.");
        }

        try
        {
            var diff = await gitHubApiService.GetPullRequestDiffAsync(
                repository[0],
                repository[1],
                review.PullRequestNumber,
                accessToken,
                cancellationToken);
            var login = User.FindFirstValue("github:login") ?? User.Identity?.Name ?? "github-user";
            var reviewRun = await reviewAnalysisOrchestrator.AnalyzeAndPersistAsync(
                githubUserId,
                login,
                review.RepositoryFullName,
                diff,
                cancellationToken);
            return Ok(new PullRequestReviewResponse(reviewRun, diff));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to retry review {ReviewRunId}.", reviewRunId);
            return TypedProblem(
                "Unable to retry review.",
                "PullSight could not complete the retry.",
                StatusCodes.Status500InternalServerError,
                "review_retry_failed");
        }
    }

    [Authorize]
    [HttpPost("{reviewRunId:guid}/publish")]
    public async Task<ActionResult<ReviewPublishResponse>> PublishReview(
        Guid reviewRunId,
        CancellationToken cancellationToken)
    {
        if (!TryGetGitHubUserId(out var githubUserId))
        {
            return Unauthorized();
        }

        var accessToken = await HttpContext.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return TypedProblem(
                "GitHub token is missing.",
                "Log in with GitHub again before publishing a review.",
                StatusCodes.Status401Unauthorized,
                "github_token_missing");
        }

        var result = await reviewPublishService.PublishReviewAsync(
            githubUserId,
            reviewRunId,
            accessToken,
            cancellationToken);

        return ToPublishResult(result);
    }

    [Authorize]
    [HttpPost("compare/publish")]
    public async Task<ActionResult<ReviewPublishResponse>> PublishComparison(
        ReviewComparisonPublishRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetGitHubUserId(out var githubUserId))
        {
            return Unauthorized();
        }

        var accessToken = await HttpContext.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return TypedProblem(
                "GitHub token is missing.",
                "Log in with GitHub again before publishing a comparison.",
                StatusCodes.Status401Unauthorized,
                "github_token_missing");
        }

        var result = await reviewPublishService.PublishComparisonAsync(
            githubUserId,
            request,
            accessToken,
            cancellationToken);

        return ToPublishResult(result);
    }

    [Authorize]
    [HttpGet("{reviewRunId:guid}")]
    public async Task<ActionResult<ReviewHistoryDetailResponse>> GetHistoryDetail(
        Guid reviewRunId,
        CancellationToken cancellationToken)
    {
        if (!TryGetGitHubUserId(out var githubUserId))
        {
            return Unauthorized();
        }

        var review = await reviewHistoryService.GetReviewAsync(
            githubUserId,
            reviewRunId,
            cancellationToken);

        return review is null ? NotFound() : Ok(review);
    }

    [HttpPost("demo")]
    public async Task<ActionResult<ReviewRunResponse>> AnalyzeDemo(CancellationToken cancellationToken)
    {
        var result = await demoAnalyzer.AnalyzeDemoAsync(cancellationToken);

        return Ok(result);
    }

    [Authorize]
    [HttpPost("github")]
    public async Task<ActionResult<PullRequestReviewResponse>> AnalyzePullRequest(
        ReviewPullRequestRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Owner) || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Repository owner and name are required.");
        }

        if (request.Number <= 0)
        {
            return BadRequest("Pull request number must be greater than zero.");
        }

        var accessToken = await HttpContext.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Problem(
                title: "GitHub token is missing.",
                detail: "Log in with GitHub again so PullSight can analyze pull requests.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!TryGetGitHubUserId(out var parsedGitHubUserId))
        {
            return Problem(
                title: "GitHub user id is missing.",
                detail: "Log in with GitHub again so PullSight can persist review results.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var login = User.FindFirstValue("github:login") ?? User.Identity?.Name ?? "github-user";
        GitHubPullRequestDiffResponse diff;
        ReviewRunResponse reviewRun;

        try
        {
            diff = await gitHubApiService.GetPullRequestDiffAsync(
                request.Owner,
                request.Name,
                request.Number,
                accessToken,
                cancellationToken);
            reviewRun = await reviewAnalysisOrchestrator.AnalyzeAndPersistAsync(
                parsedGitHubUserId,
                login,
                $"{request.Owner}/{request.Name}",
                diff,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Failed to analyze PR {Owner}/{Name}#{Number}.",
                request.Owner,
                request.Name,
                request.Number);

            return Problem(
                title: "Unable to analyze pull request.",
                detail: "PullSight could not finish this review run. Check the backend logs for the saved exception.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        return Ok(new PullRequestReviewResponse(reviewRun, diff));
    }

    private bool TryGetGitHubUserId(out long githubUserId)
    {
        return long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out githubUserId);
    }

    private IActionResult ToExportResult(ReviewReportResult result)
    {
        if (result.IsSuccess)
        {
            return File(result.Content!, result.ContentType!, result.FileName!);
        }

        var statusCode = result.ErrorCode == "review_not_found"
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status400BadRequest;

        return TypedProblem(
            "Unable to export review report.",
            result.ErrorMessage!,
            statusCode,
            result.ErrorCode!);
    }

    private ActionResult<ReviewPublishResponse> ToPublishResult(ReviewPublishResult result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Response);
        }

        var statusCode = result.ErrorCode switch
        {
            "review_not_found" => StatusCodes.Status404NotFound,
            "reviews_not_same_pull_request" => StatusCodes.Status400BadRequest,
            "github_repository_unavailable" => StatusCodes.Status404NotFound,
            "review_not_completed" => StatusCodes.Status409Conflict,
            "github_comment_failed" => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status400BadRequest,
        };

        return TypedProblem(
            "Unable to publish PullSight report.",
            result.ErrorMessage!,
            statusCode,
            result.ErrorCode!);
    }

    private ObjectResult TypedGitHubPublishProblem(string code, string detail)
    {
        var statusCode = code switch
        {
            "review_not_found" or "finding_not_found" => StatusCodes.Status404NotFound,
            "review_not_completed" or "review_head_outdated" => StatusCodes.Status409Conflict,
            "github_token_missing" => StatusCodes.Status401Unauthorized,
            "github_repository_unavailable" => StatusCodes.Status404NotFound,
            "github_check_failed" or "github_inline_comment_failed" => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status400BadRequest,
        };
        return TypedProblem("Unable to publish to GitHub.", detail, statusCode, code);
    }

    private ObjectResult TypedProblem(string title, string detail, int statusCode, string code)
    {
        return Problem(
            title: title,
            detail: detail,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?> { ["code"] = code });
    }
}
