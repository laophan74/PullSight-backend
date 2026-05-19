namespace PullSight.Api.Contracts.Auth;

public sealed record AuthUserResponse(
    string Id,
    string Login,
    string? Name,
    string? Email,
    string AvatarUrl,
    string ProfileUrl);
