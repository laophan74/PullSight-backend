using PullSight.Api.Contracts.GitHub;

namespace PullSight.Api.Contracts.Reviews;

public sealed record PullRequestReviewResponse(
    ReviewRunResponse ReviewRun,
    GitHubPullRequestDiffResponse Diff);
