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
        var context = await persistenceService.EnsureContextAsync(
            githubUserId,
            login,
            pullRequestDiff,
            cancellationToken);
        var quotaRemaining = await quotaService.GetGeminiReviewsRemainingAsync(
            context.UserId,
            cancellationToken);
        var cachedReview = await persistenceService.GetCachedReviewAsync(
            context.RepositoryId,
            pullRequestDiff.Number,
            pullRequestDiff.HeadSha,
            quotaRemaining,
            cancellationToken);

        if (cachedReview is not null)
        {
            return cachedReview;
        }

        ReviewRunResponse reviewRun;

        if (!geminiAnalyzer.IsConfigured)
        {
            reviewRun = await fallbackAnalyzer.AnalyzeAsync(repositoryName, pullRequestDiff, cancellationToken);

            return await persistenceService.SaveReviewRunAsync(
                context.UserId,
                context.RepositoryId,
                reviewRun,
                quotaRemaining,
                cancellationToken);
        }

        var reservation = await quotaService.TryReserveGeminiReviewAsync(context.UserId, cancellationToken);
        quotaRemaining = reservation.Remaining;

        if (!reservation.WasReserved)
        {
            reviewRun = await fallbackAnalyzer.AnalyzeAsync(repositoryName, pullRequestDiff, cancellationToken);

            return await persistenceService.SaveReviewRunAsync(
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

        return await persistenceService.SaveReviewRunAsync(
            context.UserId,
            context.RepositoryId,
            reviewRun,
            quotaRemaining,
            cancellationToken);
    }

    private static ReviewRunResponse WithQuotaSummary(ReviewRunResponse reviewRun)
    {
        return reviewRun with
        {
            Summary = $"{reviewRun.Summary} Gemini daily quota is exhausted, so PullSight used the rule-based fallback."
        };
    }
}
