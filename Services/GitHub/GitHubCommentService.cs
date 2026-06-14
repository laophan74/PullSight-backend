using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PullSight.Api.Services.GitHub;

public sealed class GitHubCommentService(HttpClient httpClient)
{
    internal const int MaxCommentLength = 60_000;
    private const int PerPage = 100;
    private const int MaxPages = 10;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<GitHubCommentResult> UpsertIssueCommentAsync(
        string repositoryFullName,
        int pullRequestNumber,
        string marker,
        string body,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var repositoryParts = repositoryFullName.Split('/', 2);
        if (repositoryParts.Length != 2)
        {
            return GitHubCommentResult.RepositoryUnavailable();
        }

        var owner = Uri.EscapeDataString(repositoryParts[0]);
        var name = Uri.EscapeDataString(repositoryParts[1]);
        var safeBody = TruncateComment(body, marker);

        try
        {
            for (var page = 1; page <= MaxPages; page++)
            {
                using var listRequest = CreateRequest(
                    HttpMethod.Get,
                    $"https://api.github.com/repos/{owner}/{name}/issues/{pullRequestNumber}/comments?per_page={PerPage}&page={page}",
                    accessToken);
                using var listResponse = await httpClient.SendAsync(listRequest, cancellationToken);
                if (listResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    return GitHubCommentResult.RepositoryUnavailable();
                }

                if (!listResponse.IsSuccessStatusCode)
                {
                    return GitHubCommentResult.Failed();
                }

                var comments = await listResponse.Content.ReadFromJsonAsync<IReadOnlyList<GitHubIssueComment>>(
                    JsonOptions,
                    cancellationToken) ?? [];
                var existing = comments.FirstOrDefault(comment => comment.Body.Contains(marker, StringComparison.Ordinal));

                if (existing is not null)
                {
                    using var updateRequest = CreateRequest(
                        HttpMethod.Patch,
                        $"https://api.github.com/repos/{owner}/{name}/issues/comments/{existing.Id}",
                        accessToken);
                    updateRequest.Content = JsonContent.Create(new { body = safeBody });
                    using var updateResponse = await httpClient.SendAsync(updateRequest, cancellationToken);
                    if (!updateResponse.IsSuccessStatusCode)
                    {
                        return GitHubCommentResult.Failed();
                    }

                    var updated = await updateResponse.Content.ReadFromJsonAsync<GitHubIssueComment>(
                        JsonOptions,
                        cancellationToken);
                    return updated is null
                        ? GitHubCommentResult.Failed()
                        : GitHubCommentResult.Success("updated", updated.Id, updated.HtmlUrl);
                }

                if (comments.Count < PerPage)
                {
                    break;
                }
            }

            using var createRequest = CreateRequest(
                HttpMethod.Post,
                $"https://api.github.com/repos/{owner}/{name}/issues/{pullRequestNumber}/comments",
                accessToken);
            createRequest.Content = JsonContent.Create(new { body = safeBody });
            using var createResponse = await httpClient.SendAsync(createRequest, cancellationToken);
            if (createResponse.StatusCode == HttpStatusCode.NotFound)
            {
                return GitHubCommentResult.RepositoryUnavailable();
            }

            if (!createResponse.IsSuccessStatusCode)
            {
                return GitHubCommentResult.Failed();
            }

            var created = await createResponse.Content.ReadFromJsonAsync<GitHubIssueComment>(
                JsonOptions,
                cancellationToken);
            return created is null
                ? GitHubCommentResult.Failed()
                : GitHubCommentResult.Success("created", created.Id, created.HtmlUrl);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return GitHubCommentResult.Failed();
        }
    }

    internal static string TruncateComment(string body, string marker)
    {
        if (body.Length <= MaxCommentLength)
        {
            return body;
        }

        const string notice = "\n\n_Comment truncated by PullSight._\n\n";
        var contentLength = MaxCommentLength - notice.Length - marker.Length;
        return body[..Math.Max(contentLength, 0)] + notice + marker;
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string uri, string accessToken)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("PullSight", "1.0"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    private sealed record GitHubIssueComment(
        long Id,
        string Body,
        [property: JsonPropertyName("html_url")]
        string HtmlUrl);
}

public sealed record GitHubCommentResult(
    bool IsSuccess,
    string? Status,
    long? CommentId,
    string? CommentUrl,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static GitHubCommentResult Success(string status, long commentId, string commentUrl) =>
        new(true, status, commentId, commentUrl, null, null);

    public static GitHubCommentResult RepositoryUnavailable() =>
        new(false, null, null, null, "github_repository_unavailable", "The GitHub repository or pull request is unavailable.");

    public static GitHubCommentResult Failed() =>
        new(false, null, null, null, "github_comment_failed", "GitHub could not create or update the PullSight comment.");
}
