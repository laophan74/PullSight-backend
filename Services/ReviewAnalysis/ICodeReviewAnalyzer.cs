using PullSight.Api.Contracts.Reviews;
using PullSight.Api.Contracts.GitHub;

namespace PullSight.Api.Services.ReviewAnalysis;

public interface ICodeReviewAnalyzer
{
    Task<ReviewRunResponse> AnalyzeAsync(
        string repositoryName,
        GitHubPullRequestDiffResponse pullRequestDiff,
        CancellationToken cancellationToken);

    Task<ReviewRunResponse> AnalyzeDemoAsync(CancellationToken cancellationToken);
}
