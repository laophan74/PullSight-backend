namespace PullSight.Api.Contracts.Reviews;

public sealed record ReviewHistoryPageResponse(
    IReadOnlyList<ReviewHistoryItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
