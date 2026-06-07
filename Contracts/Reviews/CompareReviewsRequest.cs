namespace PullSight.Api.Contracts.Reviews;

public sealed record CompareReviewsRequest(
    string BaseReviewRunId,
    string TargetReviewRunId);
