namespace PullSight.Api.Services.ReviewAnalysis;

public sealed class GeminiOptions
{
    public string? ApiKey { get; init; }

    public string Model { get; init; } = "gemini-2.5-flash";

    public int DailyLimit { get; init; } = 5;
}
