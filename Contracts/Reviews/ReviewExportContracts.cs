namespace PullSight.Api.Contracts.Reviews;

public sealed record ReviewExportRequest(string Format = "markdown");

public sealed record ReviewComparisonExportRequest(
    string BaseReviewRunId,
    string TargetReviewRunId,
    string Format = "markdown");

public sealed record ReviewComparisonPublishRequest(
    string BaseReviewRunId,
    string TargetReviewRunId);

public sealed record ReviewPublishResponse(
    string Status,
    long CommentId,
    string CommentUrl);
