using Microsoft.EntityFrameworkCore;
using PullSight.Api.Contracts.Reviews;
using PullSight.Api.Data;

namespace PullSight.Api.Services.ReviewAnalysis;

public sealed class ReviewHistoryService(PullSightDbContext dbContext)
{
    public async Task<ReviewHistoryPageResponse> GetReviewsAsync(
        long githubUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 50);
        var userId = await GetUserIdAsync(githubUserId, cancellationToken);

        if (userId is null)
        {
            return new ReviewHistoryPageResponse([], normalizedPage, normalizedPageSize, 0, 0);
        }

        var query = dbContext.ReviewRuns
            .AsNoTracking()
            .Where(run => run.UserId == userId.Value);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(run => run.CreatedAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(run => new ReviewHistoryItemResponse(
                run.Id.ToString("N"),
                run.Repository!.FullName,
                run.PullRequestNumber,
                run.HeadSha,
                run.Status,
                run.Source,
                run.Analyzer,
                run.RiskScore,
                run.Summary ?? "Review completed.",
                run.Findings.Count,
                run.CreatedAt))
            .ToListAsync(cancellationToken);
        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)normalizedPageSize);

        return new ReviewHistoryPageResponse(
            items,
            normalizedPage,
            normalizedPageSize,
            totalCount,
            totalPages);
    }

    public async Task<ReviewHistoryDetailResponse?> GetReviewAsync(
        long githubUserId,
        Guid reviewRunId,
        CancellationToken cancellationToken)
    {
        var userId = await GetUserIdAsync(githubUserId, cancellationToken);
        if (userId is null)
        {
            return null;
        }

        return await dbContext.ReviewRuns
            .AsNoTracking()
            .Where(run => run.Id == reviewRunId && run.UserId == userId.Value)
            .Select(run => new ReviewHistoryDetailResponse(
                run.Id.ToString("N"),
                run.Repository!.FullName,
                run.PullRequestNumber,
                run.HeadSha,
                run.Status,
                run.Source,
                run.Analyzer,
                run.RiskScore,
                run.Summary ?? "Review completed.",
                run.Findings.Count,
                run.CreatedAt,
                run.Findings
                    .OrderBy(finding => finding.CreatedAt)
                    .Select(finding => new ReviewFindingResponse(
                        finding.Id.ToString("N"),
                        finding.Severity,
                        finding.FilePath ?? "pull-request",
                        finding.LineNumber ?? 1,
                        finding.Title,
                        finding.Message,
                        finding.RuleId ?? run.Source))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Guid?> GetUserIdAsync(
        long githubUserId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Users
            .AsNoTracking()
            .Where(user => user.GitHubUserId == githubUserId)
            .Select(user => (Guid?)user.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
