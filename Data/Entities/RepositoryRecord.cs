namespace PullSight.Api.Data.Entities;

public sealed class RepositoryRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public long GitHubRepositoryId { get; set; }

    public required string Owner { get; set; }

    public required string Name { get; set; }

    public required string FullName { get; set; }

    public string? DefaultBranch { get; set; }

    public bool IsPrivate { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<PullRequestRecord> PullRequests { get; set; } = [];

    public List<ReviewRun> ReviewRuns { get; set; } = [];
}
