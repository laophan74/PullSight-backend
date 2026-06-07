using Microsoft.EntityFrameworkCore;
using PullSight.Api.Contracts.Reviews;
using PullSight.Api.Data;
using PullSight.Api.Data.Entities;

namespace PullSight.Api.Services.ReviewAnalysis;

public sealed class ReviewComparisonService(PullSightDbContext dbContext)
{
    public async Task<ReviewComparisonResult> CompareAsync(
        long githubUserId,
        Guid baseReviewRunId,
        Guid targetReviewRunId,
        CancellationToken cancellationToken)
    {
        if (baseReviewRunId == targetReviewRunId)
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
                && (run.Id == baseReviewRunId || run.Id == targetReviewRunId))
            .Include(run => run.Repository)
            .Include(run => run.Findings)
            .ToListAsync(cancellationToken);

        var baseRun = runs.SingleOrDefault(run => run.Id == baseReviewRunId);
        var targetRun = runs.SingleOrDefault(run => run.Id == targetReviewRunId);

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

        return ReviewComparisonResult.Success(CompareRuns(baseRun, targetRun));
    }

    internal static ReviewComparisonResponse CompareRuns(ReviewRun baseRun, ReviewRun targetRun)
    {
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
            MapRun(baseRun),
            MapRun(targetRun),
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

    private static ReviewComparisonRunResponse MapRun(ReviewRun run)
    {
        return new ReviewComparisonRunResponse(
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
            finding.RuleId ?? runSource);
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
