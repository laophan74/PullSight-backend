namespace PullSight.Api.Data.Entities;

public sealed class ReviewFinding
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ReviewRunId { get; set; }

    public required string Severity { get; set; }

    public required string Title { get; set; }

    public string? FilePath { get; set; }

    public int? LineNumber { get; set; }

    public string? RuleId { get; set; }

    public required string Message { get; set; }

    public string? Suggestion { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ReviewRun? ReviewRun { get; set; }
}
