namespace PullSight.Api.Data.Entities;

public sealed class ReviewRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public Guid RepositoryId { get; set; }

    public int PullRequestNumber { get; set; }

    public required string HeadSha { get; set; }

    public required string Analyzer { get; set; }

    public required string Source { get; set; }

    public required string Status { get; set; }

    public int RiskScore { get; set; }

    public string? Summary { get; set; }

    public bool WasCached { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public AppUser? User { get; set; }

    public RepositoryRecord? Repository { get; set; }

    public List<ReviewFinding> Findings { get; set; } = [];

    public List<AiProviderLog> AiProviderLogs { get; set; } = [];
}
