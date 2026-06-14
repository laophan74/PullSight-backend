using Microsoft.EntityFrameworkCore;
using PullSight.Api.Contracts.GitHub;
using PullSight.Api.Contracts.Reviews;
using PullSight.Api.Data;
using PullSight.Api.Data.Entities;

namespace PullSight.Api.Services.ReviewAnalysis;

public sealed class ReviewPersistenceService(
    PullSightDbContext dbContext,
    ReviewSummaryService summaryService)
{
    public ReviewPersistenceService(PullSightDbContext dbContext)
        : this(dbContext, new ReviewSummaryService())
    {
    }

    public async Task<ReviewPersistenceContext> EnsureContextAsync(
        long githubUserId,
        string login,
        GitHubPullRequestDiffResponse pullRequestDiff,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var normalizedLogin = Truncate(login, 100);
        var user = await dbContext.Users.FirstOrDefaultAsync(
            existingUser => existingUser.GitHubUserId == githubUserId,
            cancellationToken);

        user ??= await dbContext.Users.FirstOrDefaultAsync(
            existingUser => EF.Functions.ILike(existingUser.Login, normalizedLogin),
            cancellationToken);

        if (user is null)
        {
            user = new AppUser
            {
                GitHubUserId = githubUserId,
                Login = normalizedLogin,
                CreatedAt = now,
                UpdatedAt = now,
            };
            dbContext.Users.Add(user);
        }
        else
        {
            user.UpdatedAt = now;
        }

        var repository = await dbContext.Repositories.FirstOrDefaultAsync(
            existingRepository => existingRepository.GitHubRepositoryId == pullRequestDiff.RepositoryId,
            cancellationToken);
        var (owner, name) = SplitFullName(pullRequestDiff.RepositoryFullName);

        if (repository is null)
        {
            repository = new RepositoryRecord
            {
                GitHubRepositoryId = pullRequestDiff.RepositoryId,
                Owner = Truncate(owner, 100),
                Name = Truncate(name, 150),
                FullName = Truncate(pullRequestDiff.RepositoryFullName, 260),
                CreatedAt = now,
                UpdatedAt = now,
            };
            dbContext.Repositories.Add(repository);
        }
        else
        {
            repository.Owner = Truncate(owner, 100);
            repository.Name = Truncate(name, 150);
            repository.FullName = Truncate(pullRequestDiff.RepositoryFullName, 260);
            repository.UpdatedAt = now;
        }

        var pullRequest = await dbContext.PullRequests.FirstOrDefaultAsync(
            existingPullRequest =>
                existingPullRequest.RepositoryId == repository.Id
                && existingPullRequest.Number == pullRequestDiff.Number,
            cancellationToken);

        if (pullRequest is null)
        {
            pullRequest = new PullRequestRecord
            {
                RepositoryId = repository.Id,
                Number = pullRequestDiff.Number,
                Title = Truncate(pullRequestDiff.Title, 500),
                HeadSha = Truncate(pullRequestDiff.HeadSha, 80),
                CreatedAt = now,
                UpdatedAt = now,
            };
            dbContext.PullRequests.Add(pullRequest);
        }
        else
        {
            pullRequest.Title = Truncate(pullRequestDiff.Title, 500);
            pullRequest.HeadSha = Truncate(pullRequestDiff.HeadSha, 80);
            pullRequest.IsOpen = true;
            pullRequest.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ReviewPersistenceContext(user.Id, repository.Id);
    }

    public async Task<ReviewRunResponse?> GetCachedReviewAsync(
        Guid userId,
        Guid repositoryId,
        int pullRequestNumber,
        string headSha,
        int quotaRemaining,
        CancellationToken cancellationToken)
    {
        var reviewRun = await dbContext.ReviewRuns
            .AsNoTracking()
            .Include(run => run.Repository)
            .Include(run => run.Findings.OrderBy(finding => finding.CreatedAt))
            .Where(run =>
                run.RepositoryId == repositoryId
                && run.UserId == userId
                && run.PullRequestNumber == pullRequestNumber
                && run.HeadSha == headSha
                && (run.Status == "completed" || run.Status == "fallback"))
            .OrderByDescending(run => run.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return reviewRun is null
            ? null
            : ToResponse(reviewRun, "cached", quotaRemaining);
    }

    public async Task<ReviewRun> CreateQueuedReviewAsync(
        Guid userId,
        Guid repositoryId,
        string repositoryName,
        GitHubPullRequestDiffResponse diff,
        CancellationToken cancellationToken)
    {
        var reviewRun = new ReviewRun
        {
            UserId = userId,
            RepositoryId = repositoryId,
            PullRequestNumber = diff.Number,
            HeadSha = Truncate(diff.HeadSha, 80),
            Analyzer = "pending",
            Source = "pending",
            Status = "queued",
            RiskScore = 0,
            Summary = $"Review queued for {Truncate(repositoryName, 260)}.",
            WasCached = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        dbContext.ReviewRuns.Add(reviewRun);
        await dbContext.SaveChangesAsync(cancellationToken);
        return reviewRun;
    }

    public async Task SetAnalyzingAsync(Guid reviewRunId, CancellationToken cancellationToken)
    {
        var reviewRun = await dbContext.ReviewRuns.FindAsync([reviewRunId], cancellationToken)
            ?? throw new InvalidOperationException("Queued review run was not found.");
        reviewRun.Status = "analyzing";
        reviewRun.Summary = "PullSight is analyzing the pull request.";
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReviewRunResponse> CompleteReviewAsync(
        Guid reviewRunId,
        ReviewRunResponse review,
        int quotaRemaining,
        CancellationToken cancellationToken)
    {
        var reviewRun = await dbContext.ReviewRuns
            .Include(run => run.Repository)
            .Include(run => run.Findings)
            .FirstOrDefaultAsync(run => run.Id == reviewRunId, cancellationToken)
            ?? throw new InvalidOperationException("Queued review run was not found.");
        var normalizedSummary = summaryService.Normalize(
            review.SummaryDetails,
            review.Summary,
            findings: review.Findings);

        reviewRun.Analyzer = Truncate(review.Analyzer, 80);
        reviewRun.Source = Truncate(
            review.Findings.FirstOrDefault()?.Source
                ?? (review.Status == "completed" ? "ai" : "rule"),
            40);
        reviewRun.Status = ReviewRunPolicy.IsCompleted(review.Status) ? review.Status : "completed";
        reviewRun.RiskScore = Math.Clamp(review.RiskScore, 0, 100);
        reviewRun.Summary = Truncate(normalizedSummary.Overview, 4000);
        reviewRun.SummaryDetailsJson = summaryService.Serialize(normalizedSummary);
        reviewRun.ErrorMessage = null;
        reviewRun.Findings.AddRange(review.Findings.Select(finding => new ReviewFinding
        {
            Severity = Truncate(finding.Severity, 30),
            Title = Truncate(finding.Title, 300),
            FilePath = IsRealFilePath(finding.FilePath) ? Truncate(finding.FilePath, 600) : null,
            LineNumber = finding.Line > 0 ? finding.Line : null,
            RuleId = TruncateNullable(finding.Source, 120),
            Message = Truncate(finding.Detail, 4000),
            Suggestion = TruncateNullable(finding.Suggestion, 4000),
            CreatedAt = DateTimeOffset.UtcNow,
        }));
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(reviewRun, reviewRun.Status, quotaRemaining);
    }

    public async Task MarkFailedAsync(
        Guid reviewRunId,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var reviewRun = await dbContext.ReviewRuns.FindAsync([reviewRunId], cancellationToken);
        if (reviewRun is null)
        {
            return;
        }

        reviewRun.Status = "failed";
        reviewRun.Source = "system";
        reviewRun.Analyzer = "unavailable";
        reviewRun.ErrorMessage = Truncate(errorMessage, 1000);
        reviewRun.Summary = "Review failed before analysis completed.";
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private ReviewRunResponse ToResponse(
        ReviewRun reviewRun,
        string status,
        int quotaRemaining)
    {
        var repositoryName = reviewRun.Repository?.FullName ?? "unknown/repository";
        var legacySummary = reviewRun.Summary ?? "Review completed.";
        var summaryDetails = summaryService.FromStored(reviewRun.SummaryDetailsJson, legacySummary);

        return new ReviewRunResponse(
            reviewRun.Id.ToString("N"),
            repositoryName,
            reviewRun.PullRequestNumber,
            reviewRun.HeadSha,
            status,
            reviewRun.Analyzer,
            reviewRun.RiskScore,
            quotaRemaining,
            reviewRun.CreatedAt,
            legacySummary,
            summaryDetails,
            reviewRun.ErrorMessage,
            reviewRun.Findings.Select(finding => new ReviewFindingResponse(
                finding.Id.ToString("N"),
                finding.Severity,
                finding.FilePath ?? "pull-request",
                finding.LineNumber ?? 1,
                finding.Title,
                finding.Message,
                finding.RuleId ?? reviewRun.Source,
                finding.Suggestion,
                finding.FilePath is not null && finding.LineNumber is > 0)).ToList());
    }

    private static (string Owner, string Name) SplitFullName(string fullName)
    {
        var parts = fullName.Split('/', 2, StringSplitOptions.TrimEntries);

        return parts.Length == 2
            ? (parts[0], parts[1])
            : ("unknown", fullName);
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static string? TruncateNullable(string? value, int maxLength)
    {
        return value is null ? null : Truncate(value, maxLength);
    }

    private static bool IsRealFilePath(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && !string.Equals(value, "pull-request", StringComparison.OrdinalIgnoreCase);
}

public sealed record ReviewPersistenceContext(
    Guid UserId,
    Guid RepositoryId);
