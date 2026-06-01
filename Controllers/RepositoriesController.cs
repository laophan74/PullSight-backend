using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PullSight.Api.Contracts.GitHub;
using PullSight.Api.Services.GitHub;

namespace PullSight.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/repositories")]
public sealed class RepositoriesController(GitHubApiService gitHubApiService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GitHubRepositoryResponse>>> List(
        CancellationToken cancellationToken)
    {
        var accessToken = await HttpContext.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Problem(
                title: "GitHub token is missing.",
                detail: "Log in with GitHub again so PullSight can load repositories.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var repositories = await gitHubApiService.GetRepositoriesAsync(accessToken, cancellationToken);

        return Ok(repositories);
    }
}
