namespace PullSight.Api.Data.Entities;

public sealed class GitHubConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public long GitHubUserId { get; set; }

    public required string Login { get; set; }

    public string? Scopes { get; set; }

    public DateTimeOffset ConnectedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public AppUser? User { get; set; }
}
