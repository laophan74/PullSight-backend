namespace PullSight.Api.Data.Entities;

public sealed class PullRequestRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RepositoryId { get; set; }

    public int Number { get; set; }

    public required string Title { get; set; }

    public string? AuthorLogin { get; set; }

    public required string HeadSha { get; set; }

    public string? BaseBranch { get; set; }

    public string? HeadBranch { get; set; }

    public bool IsOpen { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public RepositoryRecord? Repository { get; set; }
}
