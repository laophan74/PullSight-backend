using System.Text.Json;
using PullSight.Api.Contracts.GitHub;
using PullSight.Api.Contracts.Reviews;

namespace PullSight.Api.Services.ReviewAnalysis;

public sealed class ReviewAnalysisOrchestrator(
    GeminiCodeReviewAnalyzer geminiAnalyzer,
    RuleBasedCodeReviewAnalyzer fallbackAnalyzer,
    ReviewPersistenceService persistenceService,
    ILogger<ReviewAnalysisOrchestrator> logger)
{
    public async Task<ReviewRunResponse> AnalyzeAndPersistAsync(
        long githubUserId,
        string login,
        string repositoryName,
        GitHubPullRequestDiffResponse pullRequestDiff,
        CancellationToken cancellationToken)
    {
        try
        {
            return await AnalyzeWithPersistenceAsync(
                githubUserId,
                login,
                repositoryName,
                pullRequestDiff,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var storageStage = exception is ReviewStorageException storageException
                ? storageException.Stage
                : "unknown";
            var rootException = exception.GetBaseException();

            logger.LogError(
                exception,
                "Review storage failed at {StorageStage} for {RepositoryName}#{PullRequestNumber}. Root error {RootErrorType}: {RootErrorMessage}. Returning an uncached review.",
                storageStage,
                repositoryName,
                pullRequestDiff.Number,
                rootException.GetType().Name,
                rootException.Message);

            var reviewRun = await AnalyzeWithoutPersistenceAsync(repositoryName, pullRequestDiff, cancellationToken);

            return WithStorageUnavailableSummary(reviewRun, storageStage);
        }
    }

    private async Task<ReviewRunResponse> AnalyzeWithPersistenceAsync(
        long githubUserId,
        string login,
        string repositoryName,
        GitHubPullRequestDiffResponse pullRequestDiff,
        CancellationToken cancellationToken)
    {
        ReviewPersistenceContext context;

        try
        {
            context = await persistenceService.EnsureContextAsync(
                githubUserId,
                login,
                pullRequestDiff,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ReviewStorageException("ensure-context", exception);
        }

        ReviewRunResponse? cachedReview;

        try
        {
            cachedReview = await persistenceService.GetCachedReviewAsync(
                context.RepositoryId,
                pullRequestDiff.Number,
                pullRequestDiff.HeadSha,
                0,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ReviewStorageException("cache-read", exception);
        }

        if (cachedReview is not null)
        {
            return cachedReview;
        }

        ReviewRunResponse reviewRun;

        if (!geminiAnalyzer.IsConfigured)
        {
            reviewRun = await fallbackAnalyzer.AnalyzeAsync(repositoryName, pullRequestDiff, cancellationToken);

            return await SaveReviewAsync(
                context.UserId,
                context.RepositoryId,
                reviewRun,
                0,
                cancellationToken);
        }

        try
        {
            reviewRun = await geminiAnalyzer.AnalyzeAsync(repositoryName, pullRequestDiff, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or JsonException)
        {
            logger.LogWarning(exception, "Gemini analysis failed. Falling back to rule-based analyzer.");

            reviewRun = await fallbackAnalyzer.AnalyzeAsync(repositoryName, pullRequestDiff, cancellationToken);
        }

        return await SaveReviewAsync(
            context.UserId,
            context.RepositoryId,
            reviewRun,
            0,
            cancellationToken);
    }

    private async Task<ReviewRunResponse> SaveReviewAsync(
        Guid userId,
        Guid repositoryId,
        ReviewRunResponse reviewRun,
        int quotaRemaining,
        CancellationToken cancellationToken)
    {
        try
        {
            return await persistenceService.SaveReviewRunAsync(
                userId,
                repositoryId,
                reviewRun,
                quotaRemaining,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ReviewStorageException("save-review", exception);
        }
    }

    private async Task<ReviewRunResponse> AnalyzeWithoutPersistenceAsync(
        string repositoryName,
        GitHubPullRequestDiffResponse pullRequestDiff,
        CancellationToken cancellationToken)
    {
        if (!geminiAnalyzer.IsConfigured)
        {
            return await fallbackAnalyzer.AnalyzeAsync(repositoryName, pullRequestDiff, cancellationToken);
        }

        try
        {
            return await geminiAnalyzer.AnalyzeAsync(repositoryName, pullRequestDiff, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or JsonException)
        {
            logger.LogWarning(exception, "Gemini analysis failed during uncached review. Falling back to rule-based analyzer.");

            return await fallbackAnalyzer.AnalyzeAsync(repositoryName, pullRequestDiff, cancellationToken);
        }
    }

    private static ReviewRunResponse WithStorageUnavailableSummary(
        ReviewRunResponse reviewRun,
        string? stage = null)
    {
        var stageDetail = string.IsNullOrWhiteSpace(stage) ? string.Empty : $" Stage: {stage}.";

        return reviewRun with
        {
            Summary = $"{reviewRun.Summary} Review storage is temporarily unavailable, so this result was not cached.{stageDetail}"
        };
    }

    private sealed class ReviewStorageException(string stage, Exception innerException)
        : Exception($"Review storage failed during {stage}.", innerException)
    {
        public string Stage { get; } = stage;
    }
}
