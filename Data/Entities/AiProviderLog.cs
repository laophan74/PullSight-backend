namespace PullSight.Api.Data.Entities;

public sealed class AiProviderLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ReviewRunId { get; set; }

    public required string Provider { get; set; }

    public string? Model { get; set; }

    public required string Status { get; set; }

    public int? PromptTokens { get; set; }

    public int? CompletionTokens { get; set; }

    public decimal? EstimatedCostUsd { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ReviewRun? ReviewRun { get; set; }
}
