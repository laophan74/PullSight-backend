using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using PullSight.Api.Contracts.GitHub;
using PullSight.Api.Services.ReviewAnalysis;
using Xunit;

namespace PullSight.Api.Tests;

public sealed class GeminiCodeReviewAnalyzerTests
{
    [Fact]
    public async Task AnalyzeAsync_ParsesStructuredSummary()
    {
        var analyzer = CreateAnalyzer("""
            {
              "summary": {
                "overview": "Adds validation.",
                "riskOverview": "Medium input risk.",
                "keyChanges": ["Validates input"],
                "suggestedTestPlan": ["Reject empty input"]
              },
              "riskScore": 45,
              "findings": []
            }
            """);

        var result = await analyzer.AnalyzeAsync("owner/repo", CreateDiff(), CancellationToken.None);

        Assert.Equal("Adds validation.", result.SummaryDetails.Overview);
        Assert.Equal("Reject empty input", Assert.Single(result.SummaryDetails.SuggestedTestPlan));
    }

    [Fact]
    public async Task AnalyzeAsync_UsesFallbackWhenSummaryBlockIsInvalid()
    {
        var analyzer = CreateAnalyzer("""
            {
              "summary": 42,
              "riskScore": 20,
              "findings": [{
                "severity": "low",
                "filePath": "src/Test.cs",
                "line": 1,
                "title": "Check behavior",
                "detail": "Verify the new behavior."
              }]
            }
            """);

        var result = await analyzer.AnalyzeAsync("owner/repo", CreateDiff(), CancellationToken.None);

        Assert.Single(result.Findings);
        Assert.NotEmpty(result.SummaryDetails.Overview);
        Assert.NotEmpty(result.SummaryDetails.SuggestedTestPlan);
    }

    private static GeminiCodeReviewAnalyzer CreateAnalyzer(string analysisJson)
    {
        var responseJson = $$"""
            {
              "candidates": [{
                "content": {
                  "role": "model",
                  "parts": [{ "text": {{System.Text.Json.JsonSerializer.Serialize(analysisJson)}} }]
                }
              }]
            }
            """;
        var handler = new StaticHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
        });
        return new GeminiCodeReviewAnalyzer(
            new HttpClient(handler),
            Options.Create(new GeminiOptions { ApiKey = "test-key", Model = "test-model" }),
            new ReviewSummaryService());
    }

    private static GitHubPullRequestDiffResponse CreateDiff() => new(
        99,
        "owner/repo",
        100,
        12,
        "Test PR",
        "abcdef123456",
        1,
        1,
        0,
        [
            new GitHubPullRequestFileResponse(
                "sha",
                "src/Test.cs",
                "modified",
                1,
                0,
                1,
                "@@ -1 +1 @@\n+new",
                "https://example.test/blob",
                "https://example.test/raw",
                null),
        ]);

    private sealed class StaticHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response);
    }
}

