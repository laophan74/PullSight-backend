namespace PullSight.Api.Contracts.Reviews;

public sealed record ReviewRunResponse(
    string Id,
    string RepositoryName,
    int PullRequestNumber,
    string HeadSha,
    string Status,
    string Analyzer,
    int RiskScore,
    int QuotaRemaining,
    string Summary,
    IReadOnlyList<ReviewFindingResponse> Findings);
