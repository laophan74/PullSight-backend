using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PullSight.Api.Services.GitHub;

public sealed class GitHubInlineCommentService(HttpClient httpClient)
{
    internal const int MaxCommentLength = 60_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<GitHubInlineCommentResult> UpsertAsync(
        string repositoryFullName,
        int pullRequestNumber,
        string headSha,
        string filePath,
        int line,
        string marker,
        string body,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var parts = repositoryFullName.Split('/', 2);
        if (parts.Length != 2)
        {
            return GitHubInlineCommentResult.RepositoryUnavailable();
        }

        var owner = Uri.EscapeDataString(parts[0]);
        var name = Uri.EscapeDataString(parts[1]);
        var safeBody = Truncate(body, marker);

        try
        {
            using var listRequest = CreateRequest(
                HttpMethod.Get,
                $"https://api.github.com/repos/{owner}/{name}/pulls/{pullRequestNumber}/comments?per_page=100",
                accessToken);
            using var listResponse = await httpClient.SendAsync(listRequest, cancellationToken);
            if (listResponse.StatusCode == HttpStatusCode.NotFound)
            {
                return GitHubInlineCommentResult.RepositoryUnavailable();
            }

            if (!listResponse.IsSuccessStatusCode)
            {
                return GitHubInlineCommentResult.Failed();
            }

            var comments = await listResponse.Content.ReadFromJsonAsync<IReadOnlyList<GitHubReviewComment>>(
                JsonOptions,
                cancellationToken) ?? [];
            var existing = comments.FirstOrDefault(comment =>
                comment.Body.Contains(marker, StringComparison.Ordinal));
            if (existing is not null)
            {
                using var updateRequest = CreateRequest(
                    HttpMethod.Patch,
                    $"https://api.github.com/repos/{owner}/{name}/pulls/comments/{existing.Id}",
                    accessToken);
                updateRequest.Content = JsonContent.Create(new { body = safeBody });
                using var updateResponse = await httpClient.SendAsync(updateRequest, cancellationToken);
                if (updateResponse.StatusCode is HttpStatusCode.UnprocessableEntity
                    or HttpStatusCode.MethodNotAllowed)
                {
                    return GitHubInlineCommentResult.AlreadyPublished(existing.Id, existing.HtmlUrl);
                }

                if (!updateResponse.IsSuccessStatusCode)
                {
                    return GitHubInlineCommentResult.Failed();
                }

                var updated = await updateResponse.Content.ReadFromJsonAsync<GitHubReviewComment>(
                    JsonOptions,
                    cancellationToken);
                return updated is null
                    ? GitHubInlineCommentResult.Failed()
                    : GitHubInlineCommentResult.Success("updated", updated.Id, updated.HtmlUrl);
            }

            using var createRequest = CreateRequest(
                HttpMethod.Post,
                $"https://api.github.com/repos/{owner}/{name}/pulls/{pullRequestNumber}/comments",
                accessToken);
            createRequest.Content = JsonContent.Create(new
            {
                body = safeBody,
                commit_id = headSha,
                path = filePath,
                line,
                side = "RIGHT",
            });
            using var createResponse = await httpClient.SendAsync(createRequest, cancellationToken);
            if (createResponse.StatusCode == HttpStatusCode.NotFound)
            {
                return GitHubInlineCommentResult.RepositoryUnavailable();
            }

            if (!createResponse.IsSuccessStatusCode)
            {
                return GitHubInlineCommentResult.Failed();
            }

            var created = await createResponse.Content.ReadFromJsonAsync<GitHubReviewComment>(
                JsonOptions,
                cancellationToken);
            return created is null
                ? GitHubInlineCommentResult.Failed()
                : GitHubInlineCommentResult.Success("created", created.Id, created.HtmlUrl);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return GitHubInlineCommentResult.Failed();
        }
    }

    internal static string Truncate(string body, string marker)
    {
        if (body.Length <= MaxCommentLength)
        {
            return body;
        }

        const string notice = "\n\n_Comment truncated by PullSight._\n\n";
        var length = Math.Max(0, MaxCommentLength - notice.Length - marker.Length);
        return body[..length] + notice + marker;
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string uri,
        string accessToken)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("PullSight", "1.0"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    private sealed record GitHubReviewComment(
        long Id,
        string Body,
        [property: JsonPropertyName("html_url")]
        string HtmlUrl);
}

public sealed record GitHubInlineCommentResult(
    string Status,
    long? CommentId,
    string? CommentUrl,
    string? ErrorCode,
    string? ErrorMessage)
{
    public bool IsSuccess => Status is "created" or "updated" or "alreadyPublished";

    public static GitHubInlineCommentResult Success(string status, long id, string url) =>
        new(status, id, url, null, null);

    public static GitHubInlineCommentResult AlreadyPublished(long id, string url) =>
        new("alreadyPublished", id, url, null, null);

    public static GitHubInlineCommentResult RepositoryUnavailable() =>
        new("failed", null, null, "github_repository_unavailable", "The GitHub repository or pull request is unavailable.");

    public static GitHubInlineCommentResult Failed() =>
        new("failed", null, null, "github_inline_comment_failed", "GitHub could not create or update the inline comment.");
}
