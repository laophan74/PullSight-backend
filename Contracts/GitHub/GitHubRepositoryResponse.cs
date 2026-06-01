namespace PullSight.Api.Contracts.GitHub;

public sealed record GitHubRepositoryResponse(
    long Id,
    string Name,
    string Owner,
    string FullName,
    string? Description,
    string? Language,
    string Visibility,
    bool IsPrivate,
    string DefaultBranch,
    int OpenPullRequests,
    DateTimeOffset? PushedAt,
    DateTimeOffset LastSyncedAt,
    string HtmlUrl);
