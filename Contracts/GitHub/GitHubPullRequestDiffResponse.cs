namespace PullSight.Api.Contracts.GitHub;

public sealed record GitHubPullRequestDiffResponse(
    long Id,
    int Number,
    string Title,
    string HeadSha,
    int ChangedFiles,
    int Additions,
    int Deletions,
    IReadOnlyList<GitHubPullRequestFileResponse> Files);

public sealed record GitHubPullRequestFileResponse(
    string Sha,
    string FileName,
    string Status,
    int Additions,
    int Deletions,
    int Changes,
    string? Patch,
    string BlobUrl,
    string RawUrl,
    string? PreviousFileName);
