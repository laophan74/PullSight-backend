namespace PullSight.Api.Contracts.Reviews;

public sealed record CompareReviewsRequest(
    Guid BaseReviewRunId,
    Guid TargetReviewRunId);
