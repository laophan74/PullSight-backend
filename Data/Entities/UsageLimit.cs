namespace PullSight.Api.Data.Entities;

public sealed class UsageLimit
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public DateOnly UsageDate { get; set; }

    public int ReviewCount { get; set; }

    public int DailyLimit { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public AppUser? User { get; set; }
}
