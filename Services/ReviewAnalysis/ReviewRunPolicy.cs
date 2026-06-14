using System.Text.Json;

namespace PullSight.Api.Services.ReviewAnalysis;

public static class ReviewRunPolicy
{
    public static readonly IReadOnlySet<string> KnownStatuses = new HashSet<string>(
        ["queued", "analyzing", "completed", "fallback", "failed"],
        StringComparer.OrdinalIgnoreCase);

    public static bool IsCompleted(string status) =>
        status is "completed" or "fallback";

    public static string SanitizeError(Exception exception)
    {
        var message = exception switch
        {
            HttpRequestException => "The review provider or GitHub API was unavailable.",
            InvalidOperationException => "The review response could not be processed.",
            JsonException => "The review provider returned an invalid response.",
            _ => "PullSight could not complete this review.",
        };

        return message;
    }
}
