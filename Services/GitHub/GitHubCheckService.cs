using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PullSight.Api.Services.GitHub;

public sealed class GitHubCheckService(HttpClient httpClient)
{
    public const int MaxAnnotations = 50;
    internal const int MaxSummaryLength = 60_000;
    internal const int MaxTitleLength = 255;
    internal const int MaxAnnotationMessageLength = 64_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<GitHubCheckResult> UpsertAsync(
        string repositoryFullName,
        string headSha,
        string externalId,
        string conclusion,
        string title,
        string summary,
        IReadOnlyList<GitHubCheckAnnotation> annotations,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var parts = repositoryFullName.Split('/', 2);
        if (parts.Length != 2)
        {
            return GitHubCheckResult.RepositoryUnavailable();
        }

        var owner = Uri.EscapeDataString(parts[0]);
        var name = Uri.EscapeDataString(parts[1]);

        try
        {
            using var listRequest = CreateRequest(
                HttpMethod.Get,
                $"https://api.github.com/repos/{owner}/{name}/commits/{Uri.EscapeDataString(headSha)}/check-runs?check_name=PullSight&per_page=100",
                accessToken);
            using var listResponse = await httpClient.SendAsync(listRequest, cancellationToken);
            if (listResponse.StatusCode == HttpStatusCode.NotFound)
            {
                return GitHubCheckResult.RepositoryUnavailable();
            }

            if (!listResponse.IsSuccessStatusCode)
            {
                return GitHubCheckResult.Failed();
            }

            var listed = await listResponse.Content.ReadFromJsonAsync<GitHubCheckRunsResponse>(
                JsonOptions,
                cancellationToken);
            var existing = listed?.CheckRuns.FirstOrDefault(check =>
                string.Equals(check.ExternalId, externalId, StringComparison.Ordinal));
            var payload = new
            {
                name = "PullSight",
                head_sha = headSha,
                status = "completed",
                conclusion,
                external_id = externalId,
                output = new
                {
                    title = Truncate(title, MaxTitleLength),
                    summary = Truncate(summary, MaxSummaryLength),
                    annotations = annotations.Take(MaxAnnotations).Select(annotation => new
                    {
                        path = annotation.Path,
                        start_line = annotation.Line,
                        end_line = annotation.Line,
                        annotation_level = annotation.Level,
                        message = Truncate(annotation.Message, MaxAnnotationMessageLength),
                        title = Truncate(annotation.Title, MaxTitleLength),
                    }),
                },
            };

            var method = existing is null ? HttpMethod.Post : HttpMethod.Patch;
            var uri = existing is null
                ? $"https://api.github.com/repos/{owner}/{name}/check-runs"
                : $"https://api.github.com/repos/{owner}/{name}/check-runs/{existing.Id}";
            using var publishRequest = CreateRequest(method, uri, accessToken);
            publishRequest.Content = JsonContent.Create(payload);
            using var publishResponse = await httpClient.SendAsync(publishRequest, cancellationToken);
            if (publishResponse.StatusCode == HttpStatusCode.NotFound)
            {
                return GitHubCheckResult.RepositoryUnavailable();
            }

            if (!publishResponse.IsSuccessStatusCode)
            {
                return publishResponse.StatusCode == HttpStatusCode.Forbidden
                    ? GitHubCheckResult.RequiresGitHubApp()
                    : GitHubCheckResult.Failed();
            }

            var published = await publishResponse.Content.ReadFromJsonAsync<GitHubCheckRun>(
                JsonOptions,
                cancellationToken);
            return published is null
                ? GitHubCheckResult.Failed()
                : GitHubCheckResult.Success(
                    existing is null ? "created" : "updated",
                    published.Id,
                    published.HtmlUrl);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return GitHubCheckResult.Failed();
        }
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

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private sealed record GitHubCheckRunsResponse(
        [property: JsonPropertyName("check_runs")]
        IReadOnlyList<GitHubCheckRun> CheckRuns);

    private sealed record GitHubCheckRun(
        long Id,
        [property: JsonPropertyName("external_id")]
        string? ExternalId,
        [property: JsonPropertyName("html_url")]
        string HtmlUrl);
}

public sealed record GitHubCheckAnnotation(
    string Path,
    int Line,
    string Level,
    string Title,
    string Message);

public sealed record GitHubCheckResult(
    bool IsSuccess,
    string? Status,
    long? CheckRunId,
    string? CheckRunUrl,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static GitHubCheckResult Success(string status, long id, string url) =>
        new(true, status, id, url, null, null);

    public static GitHubCheckResult RepositoryUnavailable() =>
        new(false, null, null, null, "github_repository_unavailable", "The GitHub repository or commit is unavailable.");

    public static GitHubCheckResult RequiresGitHubApp() =>
        new(
            false,
            null,
            null,
            null,
            "github_check_requires_app",
            "GitHub Check Runs require a GitHub App installation with Checks: write permission. The current OAuth login token cannot create Check Runs.");

    public static GitHubCheckResult Failed() =>
        new(false, null, null, null, "github_check_failed", "GitHub could not create or update the PullSight check run.");
}
