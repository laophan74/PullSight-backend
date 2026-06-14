using System.Net;
using System.Text;
using System.Text.Json;
using PullSight.Api.Contracts.GitHub;
using PullSight.Api.Contracts.Reviews;
using PullSight.Api.Services.GitHub;
using PullSight.Api.Services.ReviewAnalysis;
using Xunit;

namespace PullSight.Api.Tests;

public sealed class GitHubPublishingTests
{
    [Theory]
    [InlineData("critical", "failure")]
    [InlineData("high", "failure")]
    [InlineData("medium", "neutral")]
    [InlineData("low", "success")]
    public void CheckConclusion_IsConservative(string severity, string expected)
    {
        var finding = new ReviewFindingResponse(
            "id",
            severity,
            "src/Test.cs",
            2,
            "Title",
            "Detail",
            "rule");

        Assert.Equal(expected, ReviewGitHubPublishService.GetConclusion([finding]));
    }

    [Fact]
    public void DiffMapper_OnlyAcceptsAddedRightSideLines()
    {
        var diff = CreateDiff("@@ -4,2 +4,3 @@\n context\n-old\n+new\n+second");

        Assert.False(GitHubDiffLineMapper.IsChangedRightSideLine(diff, "src/Test.cs", 4));
        Assert.True(GitHubDiffLineMapper.IsChangedRightSideLine(diff, "src/Test.cs", 5));
        Assert.True(GitHubDiffLineMapper.IsChangedRightSideLine(diff, "src/Test.cs", 6));
        Assert.False(GitHubDiffLineMapper.IsChangedRightSideLine(diff, "src/Other.cs", 5));
    }

    [Fact]
    public async Task CheckService_CreatesThenUpdatesMatchingExternalId()
    {
        var createHandler = new QueueHandler(
            Json(HttpStatusCode.OK, """{"check_runs":[]}"""),
            Json(HttpStatusCode.Created, """{"id":11,"external_id":"pullsight:abc","html_url":"https://github.test/check/11"}"""));
        var createResult = await new GitHubCheckService(new HttpClient(createHandler)).UpsertAsync(
            "owner/repo",
            "abcdef",
            "pullsight:abc",
            "success",
            "Title",
            "Summary",
            [],
            "token",
            CancellationToken.None);
        Assert.Equal("created", createResult.Status);
        Assert.Equal(HttpMethod.Post, createHandler.Methods[1]);

        var updateHandler = new QueueHandler(
            Json(HttpStatusCode.OK, """{"check_runs":[{"id":11,"external_id":"pullsight:abc","html_url":"https://github.test/check/11"}]}"""),
            Json(HttpStatusCode.OK, """{"id":11,"external_id":"pullsight:abc","html_url":"https://github.test/check/11"}"""));
        var updateResult = await new GitHubCheckService(new HttpClient(updateHandler)).UpsertAsync(
            "owner/repo",
            "abcdef",
            "pullsight:abc",
            "neutral",
            "Title",
            "Summary",
            [],
            "token",
            CancellationToken.None);
        Assert.Equal("updated", updateResult.Status);
        Assert.Equal(HttpMethod.Patch, updateHandler.Methods[1]);
    }

    [Fact]
    public async Task CheckService_BoundsAnnotationCountAndContent()
    {
        var handler = new QueueHandler(
            Json(HttpStatusCode.OK, """{"check_runs":[]}"""),
            Json(HttpStatusCode.Created, """{"id":11,"external_id":"pullsight:abc","html_url":"https://github.test/check/11"}"""));
        var annotations = Enumerable.Range(1, 60)
            .Select(index => new GitHubCheckAnnotation(
                "src/Test.cs",
                index,
                "warning",
                new string('t', 400),
                new string('m', 70_000)))
            .ToList();

        await new GitHubCheckService(new HttpClient(handler)).UpsertAsync(
            "owner/repo",
            "abcdef",
            "pullsight:abc",
            "neutral",
            new string('x', 400),
            new string('s', 70_000),
            annotations,
            "token",
            CancellationToken.None);

        using var payload = JsonDocument.Parse(handler.Bodies[1]!);
        var output = payload.RootElement.GetProperty("output");
        Assert.Equal(50, output.GetProperty("annotations").GetArrayLength());
        Assert.True(output.GetProperty("title").GetString()!.Length <= GitHubCheckService.MaxTitleLength);
        Assert.True(output.GetProperty("summary").GetString()!.Length <= GitHubCheckService.MaxSummaryLength);
    }

    [Fact]
    public async Task InlineService_UpdatesMatchingMarkerAndPreventsDuplicate()
    {
        const string marker = "<!-- pullsight-inline:run:identity -->";
        var handler = new QueueHandler(
            Json(HttpStatusCode.OK, $$"""[{"id":7,"body":"old {{marker}}","html_url":"https://github.test/comment/7"}]"""),
            new HttpResponseMessage(HttpStatusCode.UnprocessableEntity));
        var result = await new GitHubInlineCommentService(new HttpClient(handler)).UpsertAsync(
            "owner/repo",
            12,
            "abcdef",
            "src/Test.cs",
            5,
            marker,
            $"new\n{marker}",
            "token",
            CancellationToken.None);

        Assert.Equal("alreadyPublished", result.Status);
        Assert.Equal(2, handler.Methods.Count);
    }

    [Fact]
    public void StableFindingIdentity_DoesNotDependOnDatabaseId()
    {
        var first = new ReviewFindingResponse(
            "database-one",
            "high",
            "src/Test.cs",
            5,
            "Unsafe",
            "Detail",
            "rule");
        var second = first with { Id = "database-two" };

        Assert.Equal(
            ReviewGitHubPublishService.StableFindingIdentity(first),
            ReviewGitHubPublishService.StableFindingIdentity(second));
    }

    [Fact]
    public void InlineCommentTruncation_PreservesMarker()
    {
        const string marker = "<!-- pullsight-inline:run:identity -->";
        var result = GitHubInlineCommentService.Truncate(
            new string('x', GitHubInlineCommentService.MaxCommentLength + 500) + marker,
            marker);

        Assert.True(result.Length <= GitHubInlineCommentService.MaxCommentLength);
        Assert.EndsWith(marker, result);
    }

    private static GitHubPullRequestDiffResponse CreateDiff(string patch) => new(
        99,
        "owner/repo",
        100,
        12,
        "Test PR",
        "abcdef",
        1,
        2,
        1,
        [
            new GitHubPullRequestFileResponse(
                "sha",
                "src/Test.cs",
                "modified",
                2,
                1,
                3,
                patch,
                "https://example.test/blob",
                "https://example.test/raw",
                null),
        ]);

    private static HttpResponseMessage Json(HttpStatusCode status, string value) => new(status)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json"),
    };

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new(responses);

        public List<HttpMethod> Methods { get; } = [];
        public List<string?> Bodies { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            Bodies.Add(request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult());
            return Task.FromResult(responses.Dequeue());
        }
    }
}
