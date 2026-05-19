namespace PullSight.Api.Services.GitHub;

public sealed class GitHubOAuthOptions
{
    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;

    public string CallbackPath { get; init; } = "/api/auth/github/callback";

    public string Scopes { get; init; } = "read:user user:email repo";
}
