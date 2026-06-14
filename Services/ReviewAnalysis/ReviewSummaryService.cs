using System.Text.Json;
using PullSight.Api.Contracts.GitHub;
using PullSight.Api.Contracts.Reviews;

namespace PullSight.Api.Services.ReviewAnalysis;

public sealed class ReviewSummaryService
{
    private const int MaxOverviewLength = 1_000;
    private const int MaxRiskLength = 800;
    private const int MaxListItemLength = 300;
    private const int MaxListItems = 8;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ReviewSummaryResponse Normalize(
        ReviewSummaryResponse? summary,
        string legacySummary,
        GitHubPullRequestDiffResponse? diff = null,
        IReadOnlyList<ReviewFindingResponse>? findings = null)
    {
        var fallback = BuildFallback(legacySummary, diff, findings ?? []);
        if (summary is null)
        {
            return fallback;
        }

        return new ReviewSummaryResponse(
            Clean(summary.Overview, MaxOverviewLength, fallback.Overview),
            Clean(summary.RiskOverview, MaxRiskLength, fallback.RiskOverview),
            CleanList(summary.KeyChanges, fallback.KeyChanges),
            CleanList(summary.SuggestedTestPlan, fallback.SuggestedTestPlan));
    }

    public ReviewSummaryResponse BuildFallback(
        string legacySummary,
        GitHubPullRequestDiffResponse? diff,
        IReadOnlyList<ReviewFindingResponse> findings)
    {
        var important = findings
            .Where(finding => finding.Severity is "critical" or "high")
            .Take(MaxListItems)
            .ToList();
        var keyChanges = diff?.Files
            .Take(MaxListItems)
            .Select(file => $"{file.Status}: {file.FileName} (+{file.Additions}/-{file.Deletions})")
            .ToList()
            ?? findings
                .Select(finding => finding.Title)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxListItems)
                .ToList();
        var tests = findings
            .Take(MaxListItems)
            .Select(finding => $"Verify {finding.Title.ToLowerInvariant()} at {finding.FilePath}:{finding.Line}.")
            .ToList();

        if (tests.Count == 0)
        {
            tests.Add("Run the existing automated test suite for the changed files.");
        }

        return new ReviewSummaryResponse(
            Clean(legacySummary, MaxOverviewLength, "Review completed."),
            important.Count > 0
                ? $"{important.Count} high-impact finding(s) require attention before merge."
                : "No critical or high-risk findings were identified.",
            keyChanges.Count > 0 ? keyChanges : ["Review the changed files and pull request scope."],
            tests);
    }

    public string Serialize(ReviewSummaryResponse summary) =>
        JsonSerializer.Serialize(summary, JsonOptions);

    public ReviewSummaryResponse FromStored(string? json, string legacySummary)
    {
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<ReviewSummaryResponse>(json, JsonOptions);
                return Normalize(parsed, legacySummary);
            }
            catch (JsonException)
            {
                // Old or malformed detail data falls back to the legacy summary.
            }
        }

        return BuildFallback(legacySummary, null, []);
    }

    private static IReadOnlyList<string> CleanList(
        IReadOnlyList<string>? values,
        IReadOnlyList<string> fallback)
    {
        var cleaned = values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Clean(value, MaxListItemLength, string.Empty))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxListItems)
            .ToList();

        return cleaned is { Count: > 0 } ? cleaned : fallback;
    }

    private static string Clean(string? value, int maxLength, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Replace("\0", string.Empty).Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}

