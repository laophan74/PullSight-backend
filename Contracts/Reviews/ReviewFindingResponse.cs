namespace PullSight.Api.Contracts.Reviews;

public sealed record ReviewFindingResponse(
    string Id,
    string Severity,
    string FilePath,
    int Line,
    string Title,
    string Detail,
    string Source);
