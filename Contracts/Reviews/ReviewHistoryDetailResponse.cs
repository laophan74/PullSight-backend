namespace PullSight.Api.Contracts.Reviews;

public sealed record ReviewHistoryDetailResponse(
    string Id,
    string RepositoryFullName,
    int PullRequestNumber,
    string HeadSha,
    string Status,
    string Source,
    string Analyzer,
    int RiskScore,
    string Summary,
    int FindingCount,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ReviewFindingResponse> Findings);
