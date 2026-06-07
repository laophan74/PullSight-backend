using System.Text.Json;
using PullSight.Api.Contracts.GitHub;
using PullSight.Api.Contracts.Reviews;

namespace PullSight.Api.Services.ReviewAnalysis;

public sealed class ReviewAnalysisOrchestrator(
    GeminiCodeReviewAnalyzer geminiAnalyzer,
    RuleBasedCodeReviewAnalyzer fallbackAnalyzer,
    ReviewPersistenceService persistenceService,
    ReviewQuotaService quotaService,
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
            logger.LogError(
                exception,
                "Review persistence/cache failed for {RepositoryName}#{PullRequestNumber}. Returning an uncached review.",
                repositoryName,
                pullRequestDiff.Number);

            var reviewRun = await AnalyzeWithoutPersistenceAsync(repositoryName, pullRequestDiff, cancellationToken);

            var storageStage = exception is ReviewStorageException storageException
                ? storageException.Stage
                : null;

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

        int quotaRemaining;
        ReviewRunResponse? cachedReview;

        try
        {
            quotaRemaining = await quotaService.GetGeminiReviewsRemainingAsync(
                context.UserId,
                cancellationToken);
            cachedReview = await persistenceService.GetCachedReviewAsync(
                context.RepositoryId,
                pullRequestDiff.Number,
                pullRequestDiff.HeadSha,
                quotaRemaining,
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
                quotaRemaining,
                cancellationToken);
        }

        QuotaReservation reservation;

        try
        {
            reservation = await quotaService.TryReserveGeminiReviewAsync(context.UserId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ReviewStorageException("quota-reserve", exception);
        }

        quotaRemaining = reservation.Remaining;

        if (!reservation.WasReserved)
        {
            reviewRun = await fallbackAnalyzer.AnalyzeAsync(repositoryName, pullRequestDiff, cancellationToken);

            return await SaveReviewAsync(
                context.UserId,
                context.RepositoryId,
                WithQuotaSummary(reviewRun),
                quotaRemaining,
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
            quotaRemaining,
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

    private static ReviewRunResponse WithQuotaSummary(ReviewRunResponse reviewRun)
    {
        return reviewRun with
        {
            Summary = $"{reviewRun.Summary} Gemini daily quota is exhausted, so PullSight used the rule-based fallback."
        };
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
