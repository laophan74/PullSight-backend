namespace PullSight.Api.Contracts.Reviews;

public sealed record ReviewPullRequestRequest(
    string Owner,
    string Name,
    int Number);
