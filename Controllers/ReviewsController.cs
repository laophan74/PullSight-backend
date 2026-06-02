using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PullSight.Api.Contracts.Reviews;
using PullSight.Api.Services.GitHub;
using PullSight.Api.Services.ReviewAnalysis;

namespace PullSight.Api.Controllers;

[ApiController]
[Route("api/reviews")]
public sealed class ReviewsController(
    RuleBasedCodeReviewAnalyzer demoAnalyzer,
    GitHubApiService gitHubApiService,
    ReviewAnalysisOrchestrator reviewAnalysisOrchestrator) : ControllerBase
{
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

        var githubUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(githubUserId, out var parsedGitHubUserId))
        {
            return Problem(
                title: "GitHub user id is missing.",
                detail: "Log in with GitHub again so PullSight can persist review results.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var login = User.FindFirstValue("github:login") ?? User.Identity?.Name ?? "github-user";
        var diff = await gitHubApiService.GetPullRequestDiffAsync(
            request.Owner,
            request.Name,
            request.Number,
            accessToken,
            cancellationToken);
        var reviewRun = await reviewAnalysisOrchestrator.AnalyzeAndPersistAsync(
            parsedGitHubUserId,
            login,
            $"{request.Owner}/{request.Name}",
            diff,
            cancellationToken);

        return Ok(new PullRequestReviewResponse(reviewRun, diff));
    }
}
