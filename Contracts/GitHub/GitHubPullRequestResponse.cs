namespace PullSight.Api.Contracts.GitHub;

public sealed record GitHubPullRequestResponse(
    long Id,
    int Number,
    string Title,
    string Author,
    string Branch,
    string TargetBranch,
    string HeadSha,
    int ChangedFiles,
    int Additions,
    int Deletions,
    DateTimeOffset UpdatedAt,
    string HtmlUrl);
