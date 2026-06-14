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
        catch (ReviewStorageException exception)
        {
            var storageStage = exception.Stage;
            logger.LogError(
                exception,
                "Review storage failed at {StorageStage} for {RepositoryName}#{PullRequestNumber}. Returning an uncached review.",
                storageStage,
                repositoryName,
                pullRequestDiff.Number);

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
        var context = await ExecuteStorageAsync(
            "ensure-context",
            token => persistenceService.EnsureContextAsync(
                githubUserId,
                login,
                pullRequestDiff,
                token),
            cancellationToken);

        var cachedReview = await ExecuteStorageAsync(
            "cache-read",
            token => persistenceService.GetCachedReviewAsync(
                context.UserId,
                context.RepositoryId,
                pullRequestDiff.Number,
                pullRequestDiff.HeadSha,
                0,
                token),
            cancellationToken);

        if (cachedReview is not null)
        {
            return cachedReview;
        }

        var queuedRun = await ExecuteStorageAsync(
            "queue-review",
            token => persistenceService.CreateQueuedReviewAsync(
                context.UserId,
                context.RepositoryId,
                repositoryName,
                pullRequestDiff,
                token),
            cancellationToken);
        await ExecuteStorageAsync(
            "start-review",
            async token =>
            {
                await persistenceService.SetAnalyzingAsync(queuedRun.Id, token);
                return true;
            },
            cancellationToken);

        ReviewRunResponse reviewRun;
        try
        {
            if (!geminiAnalyzer.IsConfigured)
            {
                reviewRun = await fallbackAnalyzer.AnalyzeAsync(
                    repositoryName,
                    pullRequestDiff,
                    cancellationToken);
            }
            else
            {
                try
                {
                    reviewRun = await geminiAnalyzer.AnalyzeAsync(
                        repositoryName,
                        pullRequestDiff,
                        cancellationToken);
                }
                catch (Exception exception) when (
                    exception is HttpRequestException or InvalidOperationException or JsonException)
                {
                    logger.LogWarning(exception, "Gemini analysis failed. Falling back to rule-based analyzer.");
                    reviewRun = await fallbackAnalyzer.AnalyzeAsync(
                        repositoryName,
                        pullRequestDiff,
                        cancellationToken);
                }
            }

            return await ExecuteStorageAsync(
                "complete-review",
                token => persistenceService.CompleteReviewAsync(
                    queuedRun.Id,
                    reviewRun,
                    0,
                    token),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var safeError = ReviewRunPolicy.SanitizeError(exception);
            await ExecuteStorageAsync(
                "fail-review",
                async token =>
                {
                    await persistenceService.MarkFailedAsync(queuedRun.Id, safeError, token);
                    return true;
                },
                cancellationToken);
            throw;
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
            Summary = $"{reviewRun.Summary} Review storage is temporarily unavailable, so this result was not cached.{stageDetail}",
            SummaryDetails = reviewRun.SummaryDetails with
            {
                Overview = $"{reviewRun.SummaryDetails.Overview} Review storage is temporarily unavailable, so this result was not cached.{stageDetail}"
            }
        };
    }

    private static async Task<T> ExecuteStorageAsync<T>(
        string stage,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));

        try
        {
            return await operation(timeout.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ReviewStorageException(
                stage,
                new TimeoutException($"Review storage exceeded the 8 second budget during {stage}.", exception));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ReviewStorageException(stage, exception);
        }
    }

    private sealed class ReviewStorageException(string stage, Exception innerException)
        : Exception($"Review storage failed during {stage}.", innerException)
    {
        public string Stage { get; } = stage;
    }
}
