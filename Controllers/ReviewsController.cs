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
    ILogger<ReviewsController> logger) : ControllerBase
{
    [Authorize]
    [HttpGet]
    public async Task<ActionResult<ReviewHistoryPageResponse>> GetHistory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetGitHubUserId(out var githubUserId))
        {
            return Unauthorized();
        }

        var reviews = await reviewHistoryService.GetReviewsAsync(
            githubUserId,
            page,
            pageSize,
            cancellationToken);

        return Ok(reviews);
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
}
