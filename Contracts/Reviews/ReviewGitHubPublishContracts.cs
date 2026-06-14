namespace PullSight.Api.Contracts.Reviews;

public sealed record CheckRunPublishResponse(
    string Status,
    long CheckRunId,
    string CheckRunUrl,
    string Conclusion,
    int AnnotationCount);

public sealed record InlineCommentsPublishRequest(
    IReadOnlyList<string>? FindingIds = null,
    bool ImportantOnly = false);

public sealed record InlineCommentPublishItemResponse(
    string FindingId,
    string Status,
    long? CommentId,
    string? CommentUrl,
    string? ReasonCode,
    string? Message);

public sealed record InlineCommentsPublishResponse(
    IReadOnlyList<InlineCommentPublishItemResponse> Items,
    int Created,
    int Updated,
    int AlreadyPublished,
    int Skipped,
    int Failed);

