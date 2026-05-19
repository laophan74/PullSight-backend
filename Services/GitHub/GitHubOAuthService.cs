using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.WebUtilities;

namespace PullSight.Api.Services.GitHub;

public sealed class GitHubOAuthService(HttpClient httpClient, IOptions<GitHubOAuthOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly GitHubOAuthOptions options = options.Value;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(options.ClientId)
        && !string.IsNullOrWhiteSpace(options.ClientSecret);

    public Uri BuildAuthorizeUri(string redirectUri, string state)
    {
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = options.ClientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = options.Scopes,
            ["state"] = state,
        };

        return new Uri(QueryHelpers.AddQueryString("https://github.com/login/oauth/authorize", query));
    }

    public async Task<GitHubTokenResponse> ExchangeCodeAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = options.ClientId,
                ["client_secret"] = options.ClientSecret,
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
            }),
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("PullSight", "1.0"));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadFromJsonAsync<GitHubTokenResponse>(
            JsonOptions,
            cancellationToken);

        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
        {
            throw new InvalidOperationException("GitHub did not return an access token.");
        }

        return token;
    }

    public async Task<GitHubUserProfile> GetUserAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = CreateGitHubRequest(HttpMethod.Get, "https://api.github.com/user", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var user = await response.Content.ReadFromJsonAsync<GitHubUserProfile>(
            JsonOptions,
            cancellationToken);

        return user ?? throw new InvalidOperationException("GitHub did not return a user profile.");
    }

    public async Task<string?> GetPrimaryEmailAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = CreateGitHubRequest(HttpMethod.Get, "https://api.github.com/user/emails", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var emails = await response.Content.ReadFromJsonAsync<IReadOnlyList<GitHubEmailResponse>>(
            JsonOptions,
            cancellationToken);

        return emails?
            .FirstOrDefault(email => email.Primary && email.Verified)?
            .Email;
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
}

public sealed record GitHubTokenResponse(
    [property: JsonPropertyName("access_token")]
    string AccessToken,
    [property: JsonPropertyName("token_type")]
    string TokenType,
    string Scope);

public sealed record GitHubUserProfile(
    long Id,
    string Login,
    string? Name,
    string? Email,
    [property: JsonPropertyName("avatar_url")]
    string AvatarUrl,
    [property: JsonPropertyName("html_url")]
    string HtmlUrl);

public sealed record GitHubEmailResponse(
    string Email,
    bool Primary,
    bool Verified,
    string Visibility);
