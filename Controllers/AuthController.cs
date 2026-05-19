using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PullSight.Api.Data;
using PullSight.Api.Data.Entities;
using Microsoft.Extensions.Options;
using PullSight.Api.Contracts.Auth;
using PullSight.Api.Services.GitHub;

namespace PullSight.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    GitHubOAuthService gitHubOAuthService,
    PullSightDbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    IConfiguration configuration,
    IOptions<GitHubOAuthOptions> gitHubOptions) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector stateProtector = dataProtectionProvider.CreateProtector("PullSight.GitHubOAuthState.v1");

    [HttpGet("github/login")]
    public IActionResult Login([FromQuery] string? returnUrl)
    {
        if (!gitHubOAuthService.IsConfigured)
        {
            return Problem(
                title: "GitHub OAuth is not configured.",
                detail: "Set GitHub__ClientId and GitHub__ClientSecret on the backend.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var safeReturnUrl = GetSafeReturnUrl(returnUrl);
        var state = stateProtector.Protect(JsonSerializer.Serialize(
            new GitHubOAuthState(safeReturnUrl, DateTimeOffset.UtcNow),
            JsonOptions));
        var redirectUri = BuildCallbackUri();
        var authorizeUri = gitHubOAuthService.BuildAuthorizeUri(redirectUri, state);

        return Redirect(authorizeUri.ToString());
    }

    [HttpGet("github/callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        {
            return BadRequest("GitHub OAuth callback is missing code or state.");
        }

        var oauthState = ReadState(state);
        if (oauthState is null || DateTimeOffset.UtcNow - oauthState.CreatedAt > TimeSpan.FromMinutes(10))
        {
            return BadRequest("GitHub OAuth state is invalid or expired.");
        }

        var redirectUri = BuildCallbackUri();
        var token = await gitHubOAuthService.ExchangeCodeAsync(code, redirectUri, cancellationToken);
        var profile = await gitHubOAuthService.GetUserAsync(token.AccessToken, cancellationToken);
        var email = profile.Email ?? await gitHubOAuthService.GetPrimaryEmailAsync(token.AccessToken, cancellationToken);
        await UpsertGitHubUserAsync(profile, email, token.Scope, cancellationToken);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, profile.Id.ToString()),
            new(ClaimTypes.Name, profile.Login),
            new("github:login", profile.Login),
            new("github:avatar_url", profile.AvatarUrl),
            new("github:profile_url", profile.HtmlUrl),
        };

        if (!string.IsNullOrWhiteSpace(profile.Name))
        {
            claims.Add(new Claim("github:name", profile.Name));
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            claims.Add(new Claim(ClaimTypes.Email, email));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7),
        };
        authProperties.StoreTokens(
        [
            new AuthenticationToken { Name = "access_token", Value = token.AccessToken },
            new AuthenticationToken { Name = "scope", Value = token.Scope },
            new AuthenticationToken { Name = "token_type", Value = token.TokenType },
        ]);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);

        return Redirect(oauthState.ReturnUrl);
    }

    [Authorize]
    [HttpGet("me")]
    public ActionResult<AuthUserResponse> Me()
    {
        var user = new AuthUserResponse(
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            User.FindFirstValue("github:login") ?? User.Identity?.Name ?? string.Empty,
            User.FindFirstValue("github:name"),
            User.FindFirstValue(ClaimTypes.Email),
            User.FindFirstValue("github:avatar_url") ?? string.Empty,
            User.FindFirstValue("github:profile_url") ?? string.Empty);

        return Ok(user);
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return NoContent();
    }

    private GitHubOAuthState? ReadState(string protectedState)
    {
        try
        {
            var json = stateProtector.Unprotect(protectedState);

            return JsonSerializer.Deserialize<GitHubOAuthState>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private string BuildCallbackUri()
    {
        var callbackPath = gitHubOptions.Value.CallbackPath;

        return $"{Request.Scheme}://{Request.Host}{Request.PathBase}{callbackPath}";
    }

    private string GetSafeReturnUrl(string? returnUrl)
    {
        var fallback = configuration["App:FrontendUrl"] ?? "http://127.0.0.1:5173";

        if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var requestedUri))
        {
            return fallback;
        }

        var allowedOrigins = configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>()
            ?? [];

        var allowed = allowedOrigins
            .Append(fallback)
            .Where(origin => Uri.TryCreate(origin, UriKind.Absolute, out _))
            .Select(origin => new Uri(origin))
            .Any(origin =>
                string.Equals(origin.Scheme, requestedUri.Scheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(origin.Host, requestedUri.Host, StringComparison.OrdinalIgnoreCase)
                && origin.Port == requestedUri.Port);

        return allowed ? requestedUri.ToString() : fallback;
    }

    private async Task UpsertGitHubUserAsync(
        GitHubUserProfile profile,
        string? email,
        string? scopes,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var user = await dbContext.Users
            .Include(existingUser => existingUser.GitHubConnection)
            .FirstOrDefaultAsync(
                existingUser => existingUser.GitHubUserId == profile.Id,
                cancellationToken);

        if (user is null)
        {
            user = new AppUser
            {
                GitHubUserId = profile.Id,
                Login = profile.Login,
                CreatedAt = now,
            };
            dbContext.Users.Add(user);
        }

        user.Login = profile.Login;
        user.Name = profile.Name;
        user.Email = email;
        user.AvatarUrl = profile.AvatarUrl;
        user.ProfileUrl = profile.HtmlUrl;
        user.UpdatedAt = now;

        if (user.GitHubConnection is null)
        {
            user.GitHubConnection = new GitHubConnection
            {
                UserId = user.Id,
                GitHubUserId = profile.Id,
                Login = profile.Login,
                ConnectedAt = now,
            };
        }

        user.GitHubConnection.GitHubUserId = profile.Id;
        user.GitHubConnection.Login = profile.Login;
        user.GitHubConnection.Scopes = scopes;
        user.GitHubConnection.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private sealed record GitHubOAuthState(string ReturnUrl, DateTimeOffset CreatedAt);
}
