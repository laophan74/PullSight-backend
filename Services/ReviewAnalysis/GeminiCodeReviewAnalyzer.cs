using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using PullSight.Api.Contracts.GitHub;
using PullSight.Api.Contracts.Reviews;

namespace PullSight.Api.Services.ReviewAnalysis;

public sealed class GeminiCodeReviewAnalyzer(
    HttpClient httpClient,
    IOptions<GeminiOptions> options) : ICodeReviewAnalyzer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly GeminiOptions options = options.Value;

    public async Task<ReviewRunResponse> AnalyzeAsync(
        string repositoryName,
        GitHubPullRequestDiffResponse pullRequestDiff,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("Gemini API key is not configured.");
        }

        var prompt = BuildPrompt(repositoryName, pullRequestDiff);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(options.Model)}:generateContent");
        request.Headers.Add("x-goog-api-key", options.ApiKey);
        request.Content = JsonContent.Create(new GeminiGenerateContentRequest(
            [
                new GeminiContent(
                    "user",
                    [new GeminiPart(prompt)])
            ],
            new GeminiGenerationConfig("application/json", 0.2)));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var geminiResponse = await response.Content.ReadFromJsonAsync<GeminiGenerateContentResponse>(
            JsonOptions,
            cancellationToken)
            ?? throw new InvalidOperationException("Gemini returned an empty response.");

        var content = geminiResponse.Candidates.FirstOrDefault()?.Content.Parts.FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Gemini response did not include review content.");
        }

        var analysis = JsonSerializer.Deserialize<GeminiReviewAnalysis>(
            StripJsonFence(content),
            JsonOptions)
            ?? throw new InvalidOperationException("Gemini response could not be parsed.");

        var findings = analysis.Findings
            .Take(12)
            .Select((finding, index) => new ReviewFindingResponse(
                $"ai_{index + 1}",
                NormalizeSeverity(finding.Severity),
                string.IsNullOrWhiteSpace(finding.FilePath) ? "pull-request" : finding.FilePath,
                Math.Max(1, finding.Line ?? 1),
                string.IsNullOrWhiteSpace(finding.Title) ? "Review finding" : finding.Title,
                string.IsNullOrWhiteSpace(finding.Detail) ? "Gemini flagged this change for review." : finding.Detail,
                "ai"))
            .ToList();

        return new ReviewRunResponse(
            $"ai_{Guid.NewGuid():N}",
            repositoryName,
            pullRequestDiff.Number,
            pullRequestDiff.HeadSha,
            "completed",
            $"Gemini {options.Model}",
            Math.Clamp(analysis.RiskScore, 0, 100),
            5,
            string.IsNullOrWhiteSpace(analysis.Summary)
                ? "Gemini completed a review of the changed files."
                : analysis.Summary,
            findings);
    }

    public Task<ReviewRunResponse> AnalyzeDemoAsync(CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Gemini demo analysis is not supported.");
    }

    private static string BuildPrompt(string repositoryName, GitHubPullRequestDiffResponse pullRequestDiff)
    {
        var files = pullRequestDiff.Files
            .Where(file => !string.IsNullOrWhiteSpace(file.Patch))
            .Take(12)
            .Select(file => $"""
                FILE: {file.FileName}
                STATUS: {file.Status}
                ADDITIONS: {file.Additions}
                DELETIONS: {file.Deletions}
                PATCH:
                {TrimPatch(file.Patch!)}
                """);

        return $$"""
            You are PullSight, an AI code reviewer for GitHub pull requests.
            Review the changed files for bugs, security risks, missing validation, risky data handling, broken edge cases, and missing tests.
            Prefer concrete findings tied to changed files. Do not invent files or facts not present in the diff.

            Return only valid JSON matching this shape:
            {
              "summary": "short review summary",
              "riskScore": 0,
              "findings": [
                {
                  "severity": "high",
                  "filePath": "src/file.ts",
                  "line": 12,
                  "title": "specific issue title",
                  "detail": "why this matters and what to check"
                }
              ]
            }

            Allowed severity values: critical, high, medium, low.
            Use an empty findings array when there are no actionable issues.
            Risk score is 0-100.

            Repository: {{repositoryName}}
            Pull request: #{{pullRequestDiff.Number}} {{pullRequestDiff.Title}}
            Head SHA: {{pullRequestDiff.HeadSha}}
            Changed files: {{pullRequestDiff.ChangedFiles}}
            Additions: {{pullRequestDiff.Additions}}
            Deletions: {{pullRequestDiff.Deletions}}

            DIFFS:
            {{string.Join("\n\n", files)}}
            """;
    }

    private static string TrimPatch(string patch)
    {
        const int maxPatchChars = 5000;

        return patch.Length <= maxPatchChars
            ? patch
            : $"{patch[..maxPatchChars]}\n...[patch truncated for token budget]";
    }

    private static string StripJsonFence(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewLine = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (firstNewLine < 0 || lastFence <= firstNewLine)
        {
            return trimmed;
        }

        return trimmed[(firstNewLine + 1)..lastFence].Trim();
    }

    private static string NormalizeSeverity(string? severity)
    {
        return severity?.Trim().ToLowerInvariant() switch
        {
            "critical" => "critical",
            "high" => "high",
            "medium" => "medium",
            "low" => "low",
            _ => "medium",
        };
    }

    private sealed record GeminiGenerateContentRequest(
        IReadOnlyList<GeminiContent> Contents,
        GeminiGenerationConfig GenerationConfig);

    private sealed record GeminiContent(string Role, IReadOnlyList<GeminiPart> Parts);

    private sealed record GeminiPart(string Text);

    private sealed record GeminiGenerationConfig(
        [property: JsonPropertyName("response_mime_type")]
        string ResponseMimeType,
        double Temperature);

    private sealed record GeminiGenerateContentResponse(IReadOnlyList<GeminiCandidate> Candidates);

    private sealed record GeminiCandidate(GeminiContent Content);

    private sealed record GeminiReviewAnalysis(
        string Summary,
        int RiskScore,
        IReadOnlyList<GeminiReviewFinding> Findings);

    private sealed record GeminiReviewFinding(
        string? Severity,
        string? FilePath,
        int? Line,
        string? Title,
        string? Detail);
}
