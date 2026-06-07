using Microsoft.EntityFrameworkCore;
using PullSight.Api.Contracts.GitHub;
using PullSight.Api.Contracts.Reviews;
using PullSight.Api.Data;
using PullSight.Api.Data.Entities;

namespace PullSight.Api.Services.ReviewAnalysis;

public sealed class ReviewPersistenceService(PullSightDbContext dbContext)
{
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
                && run.PullRequestNumber == pullRequestNumber
                && run.HeadSha == headSha
                && (run.Status == "completed" || run.Status == "fallback"))
            .OrderByDescending(run => run.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return reviewRun is null
            ? null
            : ToResponse(reviewRun, "cached", quotaRemaining);
    }

    public async Task<ReviewRunResponse> SaveReviewRunAsync(
        Guid userId,
        Guid repositoryId,
        ReviewRunResponse review,
        int quotaRemaining,
        CancellationToken cancellationToken)
    {
        var source = review.Findings.FirstOrDefault()?.Source
            ?? (review.Status == "completed" ? "ai" : "rule");
        var reviewRun = new ReviewRun
        {
            UserId = userId,
            RepositoryId = repositoryId,
            PullRequestNumber = review.PullRequestNumber,
            HeadSha = Truncate(review.HeadSha, 80),
            Analyzer = Truncate(review.Analyzer, 80),
            Source = Truncate(source, 40),
            Status = Truncate(review.Status, 40),
            RiskScore = review.RiskScore,
            Summary = TruncateNullable(review.Summary, 4000),
            WasCached = false,
            CreatedAt = DateTimeOffset.UtcNow,
            Findings = review.Findings.Select(finding => new ReviewFinding
            {
                Severity = Truncate(finding.Severity, 30),
                Title = Truncate(finding.Title, 300),
                FilePath = TruncateNullable(finding.FilePath, 600),
                LineNumber = finding.Line,
                RuleId = TruncateNullable(finding.Source, 120),
                Message = Truncate(finding.Detail, 4000),
                CreatedAt = DateTimeOffset.UtcNow,
            }).ToList(),
        };

        dbContext.ReviewRuns.Add(reviewRun);
        await dbContext.SaveChangesAsync(cancellationToken);
        await dbContext.Entry(reviewRun).Reference(run => run.Repository).LoadAsync(cancellationToken);

        return ToResponse(reviewRun, reviewRun.Status, quotaRemaining);
    }

    private static ReviewRunResponse ToResponse(
        ReviewRun reviewRun,
        string status,
        int quotaRemaining)
    {
        var repositoryName = reviewRun.Repository?.FullName ?? "unknown/repository";

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
            reviewRun.Summary ?? "Review completed.",
            reviewRun.Findings.Select(finding => new ReviewFindingResponse(
                finding.Id.ToString("N"),
                finding.Severity,
                finding.FilePath ?? "pull-request",
                finding.LineNumber ?? 1,
                finding.Title,
                finding.Message,
                finding.RuleId ?? reviewRun.Source)).ToList());
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
}

public sealed record ReviewPersistenceContext(
    Guid UserId,
    Guid RepositoryId);
