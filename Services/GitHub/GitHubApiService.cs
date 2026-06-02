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

    public async Task<IReadOnlyList<GitHubPullRequestResponse>> GetPullRequestsAsync(
        string owner,
        string name,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var pullRequests = new List<GitHubPullRequestResponse>();
        var encodedOwner = Uri.EscapeDataString(owner);
        var encodedName = Uri.EscapeDataString(name);

        for (var page = 1; page <= MaxPages; page++)
        {
            using var request = CreateGitHubRequest(
                HttpMethod.Get,
                $"https://api.github.com/repos/{encodedOwner}/{encodedName}/pulls?state=open&sort=updated&direction=desc&per_page={PerPage}&page={page}",
                accessToken);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var pageItems = await response.Content.ReadFromJsonAsync<IReadOnlyList<GitHubPullRequestApiResponse>>(
                JsonOptions,
                cancellationToken) ?? [];

            pullRequests.AddRange(pageItems.Select(ToPullRequestResponse));

            if (pageItems.Count < PerPage)
            {
                break;
            }
        }

        return pullRequests;
    }

    public async Task<GitHubPullRequestDiffResponse> GetPullRequestDiffAsync(
        string owner,
        string name,
        int number,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var encodedOwner = Uri.EscapeDataString(owner);
        var encodedName = Uri.EscapeDataString(name);

        using var pullRequestRequest = CreateGitHubRequest(
            HttpMethod.Get,
            $"https://api.github.com/repos/{encodedOwner}/{encodedName}/pulls/{number}",
            accessToken);
        using var pullRequestResponse = await httpClient.SendAsync(pullRequestRequest, cancellationToken);
        pullRequestResponse.EnsureSuccessStatusCode();

        var pullRequest = await pullRequestResponse.Content.ReadFromJsonAsync<GitHubPullRequestApiResponse>(
            JsonOptions,
            cancellationToken)
            ?? throw new InvalidOperationException("GitHub returned an empty pull request response.");

        var files = new List<GitHubPullRequestFileResponse>();

        for (var page = 1; page <= MaxPages; page++)
        {
            using var filesRequest = CreateGitHubRequest(
                HttpMethod.Get,
                $"https://api.github.com/repos/{encodedOwner}/{encodedName}/pulls/{number}/files?per_page={PerPage}&page={page}",
                accessToken);
            using var filesResponse = await httpClient.SendAsync(filesRequest, cancellationToken);
            filesResponse.EnsureSuccessStatusCode();

            var pageItems = await filesResponse.Content.ReadFromJsonAsync<IReadOnlyList<GitHubPullRequestFileApiResponse>>(
                JsonOptions,
                cancellationToken) ?? [];

            files.AddRange(pageItems.Select(ToPullRequestFileResponse));

            if (pageItems.Count < PerPage)
            {
                break;
            }
        }

        return new GitHubPullRequestDiffResponse(
            pullRequest.Base.Repo.Id,
            pullRequest.Base.Repo.FullName,
            pullRequest.Id,
            pullRequest.Number,
            pullRequest.Title,
            pullRequest.Head.Sha,
            pullRequest.ChangedFiles,
            pullRequest.Additions,
            pullRequest.Deletions,
            files);
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

    private static GitHubPullRequestResponse ToPullRequestResponse(GitHubPullRequestApiResponse pullRequest)
    {
        return new GitHubPullRequestResponse(
            pullRequest.Id,
            pullRequest.Number,
            pullRequest.Title,
            pullRequest.User.Login,
            pullRequest.Head.Ref,
            pullRequest.Base.Ref,
            pullRequest.Head.Sha,
            pullRequest.ChangedFiles,
            pullRequest.Additions,
            pullRequest.Deletions,
            pullRequest.UpdatedAt,
            pullRequest.HtmlUrl);
    }

    private static GitHubPullRequestFileResponse ToPullRequestFileResponse(
        GitHubPullRequestFileApiResponse file)
    {
        return new GitHubPullRequestFileResponse(
            file.Sha,
            file.FileName,
            file.Status,
            file.Additions,
            file.Deletions,
            file.Changes,
            file.Patch,
            file.BlobUrl,
            file.RawUrl,
            file.PreviousFileName);
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

    private sealed record GitHubPullRequestApiResponse(
        long Id,
        int Number,
        string Title,
        GitHubPullRequestUser User,
        GitHubPullRequestRef Head,
        GitHubPullRequestRef Base,
        [property: JsonPropertyName("changed_files")]
        int ChangedFiles,
        int Additions,
        int Deletions,
        [property: JsonPropertyName("updated_at")]
        DateTimeOffset UpdatedAt,
        [property: JsonPropertyName("html_url")]
        string HtmlUrl);

    private sealed record GitHubPullRequestUser(string Login);

    private sealed record GitHubPullRequestRef(
        string Ref,
        string Sha,
        GitHubPullRequestRepository Repo);

    private sealed record GitHubPullRequestRepository(
        long Id,
        string Name,
        [property: JsonPropertyName("full_name")]
        string FullName,
        GitHubRepositoryOwner Owner,
        bool Private,
        [property: JsonPropertyName("default_branch")]
        string? DefaultBranch);

    private sealed record GitHubPullRequestFileApiResponse(
        string Sha,
        [property: JsonPropertyName("filename")]
        string FileName,
        string Status,
        int Additions,
        int Deletions,
        int Changes,
        string? Patch,
        [property: JsonPropertyName("blob_url")]
        string BlobUrl,
        [property: JsonPropertyName("raw_url")]
        string RawUrl,
        [property: JsonPropertyName("previous_filename")]
        string? PreviousFileName);
}
