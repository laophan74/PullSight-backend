using PullSight.Api.Contracts.Reviews;
using PullSight.Api.Services.GitHub;

namespace PullSight.Api.Services.ReviewAnalysis;

public sealed class ReviewPublishService(
    ReviewHistoryService reviewHistoryService,
    ReviewComparisonService reviewComparisonService,
    GitHubCommentService gitHubCommentService)
{
    public async Task<ReviewPublishResult> PublishReviewAsync(
        long githubUserId,
        Guid reviewRunId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return ReviewPublishResult.Invalid(
                "github_token_missing",
                "A GitHub access token is required.");
        }

        var review = await reviewHistoryService.GetReviewAsync(
            githubUserId,
            reviewRunId,
            cancellationToken);

        if (review is null)
        {
            return ReviewPublishResult.NotFound();
        }

        var marker = $"<!-- pullsight-review:{review.Id} -->";
        var body = ReviewReportService.BuildReviewComment(review, marker);
        return await PublishAsync(
            review.RepositoryFullName,
            review.PullRequestNumber,
            marker,
            body,
            accessToken,
            cancellationToken);
    }

    public async Task<ReviewPublishResult> PublishComparisonAsync(
        long githubUserId,
        ReviewComparisonPublishRequest request,
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return ReviewPublishResult.Invalid(
                "github_token_missing",
                "A GitHub access token is required.");
        }

        var comparison = await reviewComparisonService.CompareAsync(
            githubUserId,
            request.BaseReviewRunId,
            request.TargetReviewRunId,
            cancellationToken);

        if (comparison.Status == ReviewComparisonResultStatus.NotFound)
        {
            return ReviewPublishResult.NotFound();
        }

        if (comparison.Status != ReviewComparisonResultStatus.Success)
        {
            return ReviewPublishResult.Invalid(
                comparison.ErrorCode ?? "reviews_not_same_pull_request",
                comparison.ErrorMessage ?? "Reviews cannot be compared.");
        }

        var report = comparison.Comparison!;
        var marker = $"<!-- pullsight-comparison:{report.BaseRun.Id}:{report.TargetRun.Id} -->";
        var body = ReviewReportService.BuildComparisonComment(report, marker);
        return await PublishAsync(
            report.BaseRun.RepositoryFullName,
            report.BaseRun.PullRequestNumber,
            marker,
            body,
            accessToken,
            cancellationToken);
    }

    private async Task<ReviewPublishResult> PublishAsync(
        string repositoryFullName,
        int pullRequestNumber,
        string marker,
        string body,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var result = await gitHubCommentService.UpsertIssueCommentAsync(
            repositoryFullName,
            pullRequestNumber,
            marker,
            body,
            accessToken,
            cancellationToken);

        return result.IsSuccess
            ? ReviewPublishResult.Success(
                new ReviewPublishResponse(result.Status!, result.CommentId!.Value, result.CommentUrl!))
            : ReviewPublishResult.Invalid(result.ErrorCode!, result.ErrorMessage!);
    }
}

public sealed record ReviewPublishResult(
    ReviewPublishResponse? Response,
    string? ErrorCode,
    string? ErrorMessage)
{
    public bool IsSuccess => Response is not null;

    public static ReviewPublishResult Success(ReviewPublishResponse response) =>
        new(response, null, null);

    public static ReviewPublishResult NotFound() =>
        Invalid("review_not_found", "One or both review runs were not found.");

    public static ReviewPublishResult Invalid(string errorCode, string errorMessage) =>
        new(null, errorCode, errorMessage);
}
