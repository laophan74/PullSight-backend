using System.Net;
using System.Text;
using PullSight.Api.Services.GitHub;
using Xunit;

namespace PullSight.Api.Tests;

public sealed class GitHubCommentServiceTests
{
    [Fact]
    public async Task UpsertIssueCommentAsync_CreatesCommentWhenMarkerIsMissing()
    {
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, "[]"),
            JsonResponse(HttpStatusCode.Created, """{"id":123,"body":"report","html_url":"https://github.test/comment/123"}"""));
        var service = new GitHubCommentService(new HttpClient(handler));

        var result = await service.UpsertIssueCommentAsync(
            "owner/repo",
            12,
            "<!-- pullsight-review:abc -->",
            "report\n<!-- pullsight-review:abc -->",
            "token",
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("created", result.Status);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
    }

    [Fact]
    public async Task UpsertIssueCommentAsync_UpdatesCommentWithMatchingMarker()
    {
        const string marker = "<!-- pullsight-review:abc -->";
        var handler = new QueueHttpMessageHandler(
            JsonResponse(
                HttpStatusCode.OK,
                $$"""[{"id":42,"body":"old\n{{marker}}","html_url":"https://github.test/comment/42"}]"""),
            JsonResponse(
                HttpStatusCode.OK,
                $$"""{"id":42,"body":"new\n{{marker}}","html_url":"https://github.test/comment/42"}"""));
        var service = new GitHubCommentService(new HttpClient(handler));

        var result = await service.UpsertIssueCommentAsync(
            "owner/repo",
            12,
            marker,
            $"new\n{marker}",
            "token",
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("updated", result.Status);
        Assert.Equal(HttpMethod.Patch, handler.Requests[1].Method);
        Assert.Contains("/issues/comments/42", handler.Requests[1].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task UpsertIssueCommentAsync_ReturnsTypedFailure()
    {
        var handler = new QueueHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var service = new GitHubCommentService(new HttpClient(handler));

        var result = await service.UpsertIssueCommentAsync(
            "owner/repo",
            12,
            "<!-- marker -->",
            "report",
            "token",
            CancellationToken.None);

        Assert.Equal("github_comment_failed", result.ErrorCode);
    }

    [Fact]
    public void TruncateComment_PreservesMarkerAndLengthLimit()
    {
        const string marker = "<!-- pullsight-review:abc -->";
        var result = GitHubCommentService.TruncateComment(
            new string('x', GitHubCommentService.MaxCommentLength + 500) + marker,
            marker);

        Assert.True(result.Length <= GitHubCommentService.MaxCommentLength);
        Assert.EndsWith(marker, result);
        Assert.Contains("truncated by PullSight", result);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class QueueHttpMessageHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new(responses);

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responses.Dequeue());
        }
    }
}
