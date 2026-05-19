namespace PullSight.Api.Data.Entities;

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public long GitHubUserId { get; set; }

    public required string Login { get; set; }

    public string? Name { get; set; }

    public string? Email { get; set; }

    public string? AvatarUrl { get; set; }

    public string? ProfileUrl { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public GitHubConnection? GitHubConnection { get; set; }

    public List<ReviewRun> ReviewRuns { get; set; } = [];

    public List<UsageLimit> UsageLimits { get; set; } = [];
}
