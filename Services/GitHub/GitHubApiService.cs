using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using PullSight.Api.Contracts.GitHub;

namespace PullSight.Api.Services.GitHub;

public sealed class GitHubApiService(HttpClient httpClient)
{
    private const int PerPage = 100;
    private const int MaxPages = 10;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<GitHubRepositoryResponse>> GetRepositoriesAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var repositories = new List<GitHubRepositoryResponse>();

        for (var page = 1; page <= MaxPages; page++)
        {
            using var request = CreateGitHubRequest(
                HttpMethod.Get,
                $"https://api.github.com/user/repos?affiliation=owner,collaborator,organization_member&sort=pushed&direction=desc&per_page={PerPage}&page={page}",
                accessToken);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var pageItems = await response.Content.ReadFromJsonAsync<IReadOnlyList<GitHubRepositoryApiResponse>>(
                JsonOptions,
                cancellationToken) ?? [];

            repositories.AddRange(pageItems.Select(ToRepositoryResponse));

            if (pageItems.Count < PerPage)
            {
                break;
            }
        }

        return repositories;
    }

    private static GitHubRepositoryResponse ToRepositoryResponse(GitHubRepositoryApiResponse repository)
    {
        var visibility = repository.Private ? "private" : "public";

        return new GitHubRepositoryResponse(
            repository.Id,
            repository.Name,
            repository.Owner.Login,
            repository.FullName,
            repository.Description,
            repository.Language,
            visibility,
            repository.Private,
            repository.DefaultBranch,
            0,
            repository.PushedAt,
            DateTimeOffset.UtcNow,
            repository.HtmlUrl);
    }

    private static HttpRequestMessage CreateGitHubRequest(HttpMethod method, string uri, string accessToken)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("PullSight", "1.0"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        return request;
    }

    private sealed record GitHubRepositoryApiResponse(
        long Id,
        string Name,
        [property: JsonPropertyName("full_name")]
        string FullName,
        GitHubRepositoryOwner Owner,
        string? Description,
        string? Language,
        bool Private,
        [property: JsonPropertyName("default_branch")]
        string DefaultBranch,
        [property: JsonPropertyName("pushed_at")]
        DateTimeOffset? PushedAt,
        [property: JsonPropertyName("html_url")]
        string HtmlUrl);

    private sealed record GitHubRepositoryOwner(string Login);
}
