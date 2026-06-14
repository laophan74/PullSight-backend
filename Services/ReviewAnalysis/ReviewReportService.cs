using System.Text;
using System.Text.Json;
using PullSight.Api.Contracts.Reviews;

namespace PullSight.Api.Services.ReviewAnalysis;

public sealed class ReviewReportService(
    ReviewHistoryService reviewHistoryService,
    ReviewComparisonService reviewComparisonService)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public async Task<ReviewReportResult> ExportReviewAsync(
        long githubUserId,
        Guid reviewRunId,
        string format,
        CancellationToken cancellationToken)
    {
        if (!TryParseFormat(format, out var parsedFormat))
        {
            return ReviewReportResult.InvalidFormat();
        }

        var review = await reviewHistoryService.GetReviewAsync(
            githubUserId,
            reviewRunId,
            cancellationToken);

        if (review is null)
        {
            return ReviewReportResult.NotFound();
        }

        if (!ReviewRunPolicy.IsCompleted(review.Status))
        {
            return ReviewReportResult.Invalid(
                "review_not_completed",
                "Only completed or fallback reviews can be exported.");
        }

        var content = parsedFormat == ReviewExportFormat.Json
            ? JsonSerializer.Serialize(review, JsonOptions)
            : BuildReviewMarkdown(review);
        var extension = parsedFormat == ReviewExportFormat.Json ? "json" : "md";
        var contentType = parsedFormat == ReviewExportFormat.Json
            ? "application/json; charset=utf-8"
            : "text/markdown; charset=utf-8";

        return ReviewReportResult.Success(
            Encoding.UTF8.GetBytes(content),
            contentType,
            $"pullsight-{Slug(review.RepositoryFullName)}-pr-{review.PullRequestNumber}-{ShortSha(review.HeadSha)}.{extension}");
    }

    public async Task<ReviewReportResult> ExportComparisonAsync(
        long githubUserId,
        ReviewComparisonExportRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryParseFormat(request.Format, out var parsedFormat))
        {
            return ReviewReportResult.InvalidFormat();
        }

        var comparison = await reviewComparisonService.CompareAsync(
            githubUserId,
            request.BaseReviewRunId,
            request.TargetReviewRunId,
            cancellationToken);

        if (comparison.Status == ReviewComparisonResultStatus.NotFound)
        {
            return ReviewReportResult.NotFound();
        }

        if (comparison.Status != ReviewComparisonResultStatus.Success)
        {
            return ReviewReportResult.Invalid(
                comparison.ErrorCode ?? "reviews_not_same_pull_request",
                comparison.ErrorMessage ?? "Reviews cannot be compared.");
        }

        var report = comparison.Comparison!;
        var content = parsedFormat == ReviewExportFormat.Json
            ? JsonSerializer.Serialize(report, JsonOptions)
            : BuildComparisonMarkdown(report);
        var extension = parsedFormat == ReviewExportFormat.Json ? "json" : "md";
        var contentType = parsedFormat == ReviewExportFormat.Json
            ? "application/json; charset=utf-8"
            : "text/markdown; charset=utf-8";

        return ReviewReportResult.Success(
            Encoding.UTF8.GetBytes(content),
            contentType,
            $"pullsight-{Slug(report.BaseRun.RepositoryFullName)}-pr-{report.BaseRun.PullRequestNumber}-comparison-{ShortSha(report.BaseRun.HeadSha)}-{ShortSha(report.TargetRun.HeadSha)}.{extension}");
    }

    internal static string BuildReviewMarkdown(ReviewHistoryDetailResponse review)
    {
        var builder = new StringBuilder()
            .AppendLine("# PullSight Review Report")
            .AppendLine()
            .AppendLine($"- Repository: `{review.RepositoryFullName}`")
            .AppendLine($"- Pull request: #{review.PullRequestNumber}")
            .AppendLine($"- Head SHA: `{review.HeadSha}`")
            .AppendLine($"- Analyzer: {review.Analyzer}")
            .AppendLine($"- Source / status: {review.Source} / {review.Status}")
            .AppendLine($"- Risk score: {review.RiskScore}")
            .AppendLine($"- Created: {review.CreatedAt:O}")
            .AppendLine()
            .AppendLine("## Summary")
            .AppendLine()
            .AppendLine(review.SummaryDetails.Overview)
            .AppendLine()
            .AppendLine("### Risk overview")
            .AppendLine()
            .AppendLine(review.SummaryDetails.RiskOverview)
            .AppendLine();

        AppendList(builder, "Key changes", review.SummaryDetails.KeyChanges);
        AppendList(builder, "Suggested test plan", review.SummaryDetails.SuggestedTestPlan);
        AppendFindingGroups(builder, review.Findings);
        return builder.ToString();
    }

    internal static string BuildComparisonMarkdown(ReviewComparisonResponse comparison)
    {
        var builder = new StringBuilder()
            .AppendLine("# PullSight Comparison Report")
            .AppendLine()
            .AppendLine($"- Repository: `{comparison.BaseRun.RepositoryFullName}`")
            .AppendLine($"- Pull request: #{comparison.BaseRun.PullRequestNumber}")
            .AppendLine()
            .AppendLine("## Base")
            .AppendLine()
            .AppendLine($"- Head SHA: `{comparison.BaseRun.HeadSha}`")
            .AppendLine($"- Analyzer: {comparison.BaseRun.Analyzer}")
            .AppendLine($"- Source / status: {comparison.BaseRun.Source} / {comparison.BaseRun.Status}")
            .AppendLine($"- Risk score: {comparison.BaseRun.RiskScore}")
            .AppendLine($"- Created: {comparison.BaseRun.CreatedAt:O}")
            .AppendLine()
            .AppendLine(comparison.BaseRun.SummaryDetails.Overview)
            .AppendLine()
            .AppendLine("## Target")
            .AppendLine()
            .AppendLine($"- Head SHA: `{comparison.TargetRun.HeadSha}`")
            .AppendLine($"- Analyzer: {comparison.TargetRun.Analyzer}")
            .AppendLine($"- Source / status: {comparison.TargetRun.Source} / {comparison.TargetRun.Status}")
            .AppendLine($"- Risk score: {comparison.TargetRun.RiskScore}")
            .AppendLine($"- Created: {comparison.TargetRun.CreatedAt:O}")
            .AppendLine()
            .AppendLine(comparison.TargetRun.SummaryDetails.Overview)
            .AppendLine();

        AppendComparisonGroup(builder, "Added", comparison.Added);
        AppendComparisonGroup(builder, "Resolved", comparison.Resolved);
        AppendComparisonGroup(builder, "Unchanged", comparison.Unchanged);
        return builder.ToString();
    }

    internal static string BuildReviewComment(ReviewHistoryDetailResponse review, string marker)
    {
        var builder = new StringBuilder()
            .AppendLine("## PullSight Review")
            .AppendLine()
            .AppendLine($"**Risk:** {review.RiskScore} | **Analyzer:** {review.Analyzer} | **Head:** `{ShortSha(review.HeadSha)}`")
            .AppendLine()
            .AppendLine(review.SummaryDetails.Overview)
            .AppendLine()
            .AppendLine($"**Risk overview:** {review.SummaryDetails.RiskOverview}")
            .AppendLine()
            .AppendLine($"**Findings:** {review.Findings.Count} total ({BuildSeverityCounts(review.Findings)})");

        AppendImportantFindings(builder, review.Findings);
        AppendCompactTestPlan(builder, review.SummaryDetails.SuggestedTestPlan);
        builder.AppendLine().Append(marker);
        return builder.ToString();
    }

    internal static string BuildComparisonComment(ReviewComparisonResponse comparison, string marker)
    {
        var builder = new StringBuilder()
            .AppendLine("## PullSight Review Comparison")
            .AppendLine()
            .AppendLine($"**Base:** `{ShortSha(comparison.BaseRun.HeadSha)}` (risk {comparison.BaseRun.RiskScore})")
            .AppendLine($"**Target:** `{ShortSha(comparison.TargetRun.HeadSha)}` (risk {comparison.TargetRun.RiskScore})")
            .AppendLine()
            .AppendLine($"**Changes:** {comparison.Added.Count} added, {comparison.Resolved.Count} resolved, {comparison.Unchanged.Count} unchanged");

        AppendImportantFindings(builder, comparison.Added, "Important added findings");
        builder.AppendLine().Append(marker);
        return builder.ToString();
    }

    private static void AppendFindingGroups(StringBuilder builder, IReadOnlyList<ReviewFindingResponse> findings)
    {
        builder.AppendLine("## Findings");
        if (findings.Count == 0)
        {
            builder.AppendLine().AppendLine("No findings.");
            return;
        }

        foreach (var group in findings
                     .GroupBy(finding => finding.Severity)
                     .OrderBy(group => SeverityRank(group.Key)))
        {
            builder.AppendLine().AppendLine($"### {TitleCase(group.Key)}");
            foreach (var finding in group)
            {
                AppendFinding(builder, finding);
            }
        }
    }

    private static void AppendComparisonGroup(
        StringBuilder builder,
        string title,
        IReadOnlyList<ReviewFindingResponse> findings)
    {
        builder.AppendLine($"## {title} ({findings.Count})").AppendLine();
        if (findings.Count == 0)
        {
            builder.AppendLine("No findings.").AppendLine();
            return;
        }

        foreach (var finding in findings)
        {
            AppendFinding(builder, finding);
        }
    }

    private static void AppendFinding(StringBuilder builder, ReviewFindingResponse finding)
    {
        builder
            .AppendLine($"- **[{finding.Severity.ToUpperInvariant()}] {finding.Title}**")
            .AppendLine($"  - Location: `{finding.FilePath}:{finding.Line}`")
            .AppendLine($"  - Source: `{finding.Source}`")
            .AppendLine($"  - {finding.Detail}");
    }

    private static void AppendImportantFindings(
        StringBuilder builder,
        IReadOnlyList<ReviewFindingResponse> findings,
        string title = "Important findings")
    {
        var important = findings
            .OrderBy(finding => SeverityRank(finding.Severity))
            .ThenBy(finding => finding.FilePath)
            .Take(8)
            .ToList();

        if (important.Count == 0)
        {
            return;
        }

        builder.AppendLine().AppendLine($"### {title}");
        foreach (var finding in important)
        {
            builder.AppendLine($"- **{finding.Severity.ToUpperInvariant()}** `{finding.FilePath}:{finding.Line}` {finding.Title}");
        }
    }

    private static void AppendList(
        StringBuilder builder,
        string title,
        IReadOnlyList<string> items)
    {
        builder.AppendLine($"### {title}").AppendLine();
        foreach (var item in items)
        {
            builder.AppendLine($"- {item}");
        }

        builder.AppendLine();
    }

    private static void AppendCompactTestPlan(
        StringBuilder builder,
        IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        builder.AppendLine().AppendLine("### Suggested test plan");
        foreach (var item in items.Take(8))
        {
            builder.AppendLine($"- {item}");
        }
    }

    private static string BuildSeverityCounts(IEnumerable<ReviewFindingResponse> findings) =>
        string.Join(
            ", ",
            findings
                .GroupBy(finding => finding.Severity)
                .OrderBy(group => SeverityRank(group.Key))
                .Select(group => $"{group.Key}: {group.Count()}"));

    private static bool TryParseFormat(string? format, out ReviewExportFormat parsedFormat)
    {
        if (string.Equals(format, "markdown", StringComparison.OrdinalIgnoreCase)
            || string.Equals(format, "md", StringComparison.OrdinalIgnoreCase))
        {
            parsedFormat = ReviewExportFormat.Markdown;
            return true;
        }

        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            parsedFormat = ReviewExportFormat.Json;
            return true;
        }

        parsedFormat = default;
        return false;
    }

    private static int SeverityRank(string severity) => severity.ToLowerInvariant() switch
    {
        "critical" => 0,
        "high" => 1,
        "medium" => 2,
        _ => 3,
    };

    private static string TitleCase(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "Unknown"
            : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();

    private static string Slug(string value) =>
        string.Join("-", value.Split(['/', '\\', ' '], StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();

    private static string ShortSha(string sha) => sha[..Math.Min(10, sha.Length)];

    private enum ReviewExportFormat
    {
        Markdown,
        Json,
    }
}

public sealed record ReviewReportResult(
    byte[]? Content,
    string? ContentType,
    string? FileName,
    string? ErrorCode,
    string? ErrorMessage)
{
    public bool IsSuccess => Content is not null;

    public static ReviewReportResult Success(byte[] content, string contentType, string fileName) =>
        new(content, contentType, fileName, null, null);

    public static ReviewReportResult NotFound() =>
        new(null, null, null, "review_not_found", "One or both review runs were not found.");

    public static ReviewReportResult InvalidFormat() =>
        Invalid("invalid_export_format", "Export format must be markdown or json.");

    public static ReviewReportResult Invalid(string errorCode, string errorMessage) =>
        new(null, null, null, errorCode, errorMessage);
}
