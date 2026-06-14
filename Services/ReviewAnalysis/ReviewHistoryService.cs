using Microsoft.EntityFrameworkCore;
using PullSight.Api.Contracts.Reviews;
using PullSight.Api.Data;

namespace PullSight.Api.Services.ReviewAnalysis;

public sealed class ReviewHistoryService(
    PullSightDbContext dbContext,
    ReviewSummaryService summaryService)
{
    public ReviewHistoryService(PullSightDbContext dbContext)
        : this(dbContext, new ReviewSummaryService())
    {
    }

    public async Task<ReviewHistoryQueryResult> GetReviewsAsync(
        long githubUserId,
        ReviewHistoryQuery request,
        CancellationToken cancellationToken)
    {
        if (request.PullRequestNumber is <= 0)
        {
            return ReviewHistoryQueryResult.Invalid(
                "invalid_pull_request_number",
                "Pull request number must be greater than zero.");
        }

        if (!string.IsNullOrWhiteSpace(request.Status)
            && !ReviewRunPolicy.KnownStatuses.Contains(request.Status.Trim()))
        {
            return ReviewHistoryQueryResult.Invalid(
                "invalid_review_status",
                "Status must be queued, analyzing, completed, fallback, or failed.");
        }

        var normalizedPage = Math.Max(request.Page, 1);
        var normalizedPageSize = Math.Clamp(request.PageSize, 1, 50);
        var userId = await GetUserIdAsync(githubUserId, cancellationToken);

        if (userId is null)
        {
            return ReviewHistoryQueryResult.Success(
                new ReviewHistoryPageResponse([], normalizedPage, normalizedPageSize, 0, 0));
        }

        var query = dbContext.ReviewRuns
            .AsNoTracking()
            .Where(run => run.UserId == userId.Value);

        if (!string.IsNullOrWhiteSpace(request.Repository))
        {
            var repository = request.Repository.Trim().ToLower();
            query = query.Where(run => run.Repository!.FullName.ToLower() == repository);
        }

        if (request.PullRequestNumber is not null)
        {
            query = query.Where(run => run.PullRequestNumber == request.PullRequestNumber.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.HeadSha))
        {
            var headSha = request.HeadSha.Trim().ToLower();
            query = query.Where(run => run.HeadSha.ToLower().StartsWith(headSha));
        }

        if (!string.IsNullOrWhiteSpace(request.Source))
        {
            var source = request.Source.Trim().ToLower();
            query = query.Where(run => run.Source.ToLower() == source);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim().ToLower();
            query = query.Where(run => run.Status.ToLower() == status);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(run => run.CreatedAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(run => new
            {
                Id = run.Id.ToString("N"),
                RepositoryFullName = run.Repository!.FullName,
                run.PullRequestNumber,
                run.HeadSha,
                run.Status,
                run.Source,
                run.Analyzer,
                run.RiskScore,
                Summary = run.Summary ?? "Review completed.",
                run.SummaryDetailsJson,
                run.ErrorMessage,
                FindingCount = run.Findings.Count,
                run.CreatedAt,
            })
            .ToListAsync(cancellationToken);
        var items = rows.Select(run => new ReviewHistoryItemResponse(
            run.Id,
            run.RepositoryFullName,
            run.PullRequestNumber,
            run.HeadSha,
            run.Status,
            run.Source,
            run.Analyzer,
            run.RiskScore,
            run.Summary,
            summaryService.FromStored(run.SummaryDetailsJson, run.Summary),
            run.ErrorMessage,
            run.FindingCount,
            run.CreatedAt)).ToList();
        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)normalizedPageSize);

        return ReviewHistoryQueryResult.Success(
            new ReviewHistoryPageResponse(
                items,
                normalizedPage,
                normalizedPageSize,
                totalCount,
                totalPages));
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

        var run = await dbContext.ReviewRuns
            .AsNoTracking()
            .Include(reviewRun => reviewRun.Repository)
            .Include(reviewRun => reviewRun.Findings)
            .Where(run => run.Id == reviewRunId && run.UserId == userId.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (run is null)
        {
            return null;
        }

        var summary = run.Summary ?? "Review completed.";
        return new ReviewHistoryDetailResponse(
            run.Id.ToString("N"),
            run.Repository!.FullName,
            run.PullRequestNumber,
            run.HeadSha,
            run.Status,
            run.Source,
            run.Analyzer,
            run.RiskScore,
            summary,
            summaryService.FromStored(run.SummaryDetailsJson, summary),
            run.ErrorMessage,
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
                    finding.RuleId ?? run.Source,
                    finding.Suggestion,
                    finding.FilePath is not null && finding.LineNumber is > 0))
                .ToList());
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

public sealed record ReviewHistoryQueryResult(
    ReviewHistoryPageResponse? Page,
    string? ErrorCode,
    string? ErrorMessage)
{
    public bool IsSuccess => Page is not null;

    public static ReviewHistoryQueryResult Success(ReviewHistoryPageResponse page) =>
        new(page, null, null);

    public static ReviewHistoryQueryResult Invalid(string errorCode, string errorMessage) =>
        new(null, errorCode, errorMessage);
}
