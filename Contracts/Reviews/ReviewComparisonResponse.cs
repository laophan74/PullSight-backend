namespace PullSight.Api.Contracts.Reviews;

public sealed record ReviewComparisonResponse(
    ReviewComparisonRunResponse BaseRun,
    ReviewComparisonRunResponse TargetRun,
    IReadOnlyList<ReviewFindingResponse> Added,
    IReadOnlyList<ReviewFindingResponse> Resolved,
    IReadOnlyList<ReviewFindingResponse> Unchanged);

public sealed record ReviewComparisonRunResponse(
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
    DateTimeOffset CreatedAt);
