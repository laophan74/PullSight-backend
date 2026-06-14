using Microsoft.EntityFrameworkCore;
using PullSight.Api.Data;
using PullSight.Api.Data.Entities;

namespace PullSight.Api.Tests;

internal static class TestData
{
    public static PullSightDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PullSightDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PullSightDbContext(options);
    }

    public static AppUser CreateUser(long githubUserId) => new()
    {
        GitHubUserId = githubUserId,
        Login = $"user-{githubUserId}",
    };

    public static RepositoryRecord CreateRepository(
        string fullName = "owner/repo",
        long githubRepositoryId = 99)
    {
        var parts = fullName.Split('/', 2);
        return new RepositoryRecord
        {
            GitHubRepositoryId = githubRepositoryId,
            Owner = parts[0],
            Name = parts[1],
            FullName = fullName,
        };
    }

    public static ReviewRun CreateRun(
        AppUser user,
        RepositoryRecord repository,
        int pullRequestNumber = 12,
        string headSha = "abcdef123456",
        string source = "ai",
        string status = "completed",
        DateTimeOffset? createdAt = null)
    {
        return new ReviewRun
        {
            UserId = user.Id,
            User = user,
            RepositoryId = repository.Id,
            Repository = repository,
            PullRequestNumber = pullRequestNumber,
            HeadSha = headSha,
            Analyzer = source == "ai" ? "gemini" : "rule-based",
            Source = source,
            Status = status,
            RiskScore = 42,
            Summary = "Test review summary.",
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        };
    }

    public static ReviewFinding CreateFinding(
        string severity = "high",
        int line = 42,
        string title = "Unsafe token comparison")
    {
        return new ReviewFinding
        {
            Severity = severity,
            FilePath = "src/Auth.cs",
            LineNumber = line,
            Title = title,
            RuleId = "security-token",
            Message = "Use a constant-time comparison.",
        };
    }
}
