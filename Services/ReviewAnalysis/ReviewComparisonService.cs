using Microsoft.EntityFrameworkCore;
using PullSight.Api.Contracts.Reviews;
using PullSight.Api.Data;
using PullSight.Api.Data.Entities;

namespace PullSight.Api.Services.ReviewAnalysis;

public sealed class ReviewComparisonService(
    PullSightDbContext dbContext,
    ReviewSummaryService summaryService)
{
    public ReviewComparisonService(PullSightDbContext dbContext)
        : this(dbContext, new ReviewSummaryService())
    {
    }

    public async Task<ReviewComparisonResult> CompareAsync(
        long githubUserId,
        string baseReviewRunId,
        string targetReviewRunId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(baseReviewRunId, out var parsedBaseReviewRunId)
            || !Guid.TryParse(targetReviewRunId, out var parsedTargetReviewRunId))
        {
            return ReviewComparisonResult.Invalid(
                "invalid_review_id",
                "Both review run ids must be valid.");
        }

        if (parsedBaseReviewRunId == parsedTargetReviewRunId)
        {
            return ReviewComparisonResult.Invalid(
                "reviews_must_differ",
                "Choose two different review runs to compare.");
        }

        var userId = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.GitHubUserId == githubUserId)
            .Select(user => (Guid?)user.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (userId is null)
        {
            return ReviewComparisonResult.NotFound();
        }

        var runs = await dbContext.ReviewRuns
            .AsNoTracking()
            .Where(run =>
                run.UserId == userId.Value
                && (run.Id == parsedBaseReviewRunId || run.Id == parsedTargetReviewRunId))
            .Include(run => run.Repository)
            .Include(run => run.Findings)
            .ToListAsync(cancellationToken);

        var baseRun = runs.SingleOrDefault(run => run.Id == parsedBaseReviewRunId);
        var targetRun = runs.SingleOrDefault(run => run.Id == parsedTargetReviewRunId);

        if (baseRun is null || targetRun is null)
        {
            return ReviewComparisonResult.NotFound();
        }

        if (baseRun.RepositoryId != targetRun.RepositoryId
            || baseRun.PullRequestNumber != targetRun.PullRequestNumber)
        {
            return ReviewComparisonResult.Invalid(
                "reviews_not_same_pull_request",
                "Review runs must belong to the same repository and pull request.");
        }

        if (!ReviewRunPolicy.IsCompleted(baseRun.Status)
            || !ReviewRunPolicy.IsCompleted(targetRun.Status))
        {
            return ReviewComparisonResult.Invalid(
                "review_not_completed",
                "Only completed or fallback review runs can be compared.");
        }

        return ReviewComparisonResult.Success(CompareRuns(baseRun, targetRun, summaryService));
    }

    internal static ReviewComparisonResponse CompareRuns(
        ReviewRun baseRun,
        ReviewRun targetRun,
        ReviewSummaryService? summaryService = null)
    {
        summaryService ??= new ReviewSummaryService();
        var baseGroups = GroupFindings(baseRun.Findings);
        var targetGroups = GroupFindings(targetRun.Findings);
        var added = new List<ReviewFindingResponse>();
        var resolved = new List<ReviewFindingResponse>();
        var unchanged = new List<ReviewFindingResponse>();

        foreach (var identity in baseGroups.Keys.Union(targetGroups.Keys).Order())
        {
            var baseFindings = baseGroups.GetValueOrDefault(identity) ?? [];
            var targetFindings = targetGroups.GetValueOrDefault(identity) ?? [];
            var matchCount = Math.Min(baseFindings.Count, targetFindings.Count);

            unchanged.AddRange(targetFindings.Take(matchCount).Select(finding => MapFinding(finding, targetRun.Source)));
            resolved.AddRange(baseFindings.Skip(matchCount).Select(finding => MapFinding(finding, baseRun.Source)));
            added.AddRange(targetFindings.Skip(matchCount).Select(finding => MapFinding(finding, targetRun.Source)));
        }

        return new ReviewComparisonResponse(
            MapRun(baseRun, summaryService),
            MapRun(targetRun, summaryService),
            SortFindings(added),
            SortFindings(resolved),
            SortFindings(unchanged));
    }

    private static Dictionary<string, List<ReviewFinding>> GroupFindings(
        IEnumerable<ReviewFinding> findings)
    {
        return findings
            .OrderBy(finding => finding.CreatedAt)
            .ThenBy(finding => finding.Id)
            .GroupBy(BuildIdentity)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
    }

    private static string BuildIdentity(ReviewFinding finding)
    {
        var source = string.IsNullOrWhiteSpace(finding.RuleId) ? "unknown" : finding.RuleId;

        return string.Join(
            '\u001f',
            Normalize(finding.Severity),
            NormalizePath(finding.FilePath),
            finding.LineNumber?.ToString() ?? "0",
            Normalize(finding.Title),
            Normalize(source));
    }

    private static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static string NormalizePath(string? value)
    {
        return Normalize(value).Replace('\\', '/');
    }

    private static ReviewComparisonRunResponse MapRun(
        ReviewRun run,
        ReviewSummaryService summaryService)
    {
        var legacySummary = run.Summary ?? "Review completed.";
        return new ReviewComparisonRunResponse(
            run.Id.ToString("N"),
            run.Repository!.FullName,
            run.PullRequestNumber,
            run.HeadSha,
            run.Status,
            run.Source,
            run.Analyzer,
            run.RiskScore,
            legacySummary,
            summaryService.FromStored(run.SummaryDetailsJson, legacySummary),
            run.ErrorMessage,
            run.Findings.Count,
            run.CreatedAt);
    }

    private static ReviewFindingResponse MapFinding(ReviewFinding finding, string runSource)
    {
        return new ReviewFindingResponse(
            finding.Id.ToString("N"),
            finding.Severity,
            finding.FilePath ?? "pull-request",
            finding.LineNumber ?? 1,
            finding.Title,
            finding.Message,
            finding.RuleId ?? runSource,
            finding.Suggestion,
            finding.FilePath is not null && finding.LineNumber is > 0);
    }

    private static IReadOnlyList<ReviewFindingResponse> SortFindings(
        IEnumerable<ReviewFindingResponse> findings)
    {
        return findings
            .OrderBy(finding => finding.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(finding => finding.Line)
            .ThenBy(finding => finding.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed record ReviewComparisonResult(
    ReviewComparisonResultStatus Status,
    ReviewComparisonResponse? Comparison,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static ReviewComparisonResult Success(ReviewComparisonResponse comparison) =>
        new(ReviewComparisonResultStatus.Success, comparison, null, null);

    public static ReviewComparisonResult NotFound() =>
        new(ReviewComparisonResultStatus.NotFound, null, "review_not_found", "One or both review runs were not found.");

    public static ReviewComparisonResult Invalid(string errorCode, string errorMessage) =>
        new(ReviewComparisonResultStatus.Invalid, null, errorCode, errorMessage);
}

public enum ReviewComparisonResultStatus
{
    Success,
    NotFound,
    Invalid,
}
