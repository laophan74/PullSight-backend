using Microsoft.EntityFrameworkCore;
using PullSight.Api.Data;
using PullSight.Api.Data.Entities;
using PullSight.Api.Services.ReviewAnalysis;
using Xunit;

namespace PullSight.Api.Tests;

public sealed class ReviewComparisonServiceTests
{
    [Fact]
    public void CompareRuns_PreservesDuplicateFindingCounts()
    {
        var repository = CreateRepository();
        var baseRun = CreateRun(repository, 12, "base");
        var targetRun = CreateRun(repository, 12, "target");
        baseRun.Findings.AddRange([CreateFinding(42), CreateFinding(42)]);
        targetRun.Findings.Add(CreateFinding(42));

        var result = ReviewComparisonService.CompareRuns(baseRun, targetRun);

        Assert.Single(result.Unchanged);
        Assert.Single(result.Resolved);
        Assert.Empty(result.Added);
    }

    [Fact]
    public void CompareRuns_TreatsChangedLineAsResolvedAndAdded()
    {
        var repository = CreateRepository();
        var baseRun = CreateRun(repository, 12, "base");
        var targetRun = CreateRun(repository, 12, "target");
        baseRun.Findings.Add(CreateFinding(42));
        targetRun.Findings.Add(CreateFinding(43));

        var result = ReviewComparisonService.CompareRuns(baseRun, targetRun);

        Assert.Single(result.Resolved);
        Assert.Single(result.Added);
        Assert.Empty(result.Unchanged);
    }

    [Fact]
    public async Task CompareAsync_RejectsRunsFromDifferentPullRequests()
    {
        await using var dbContext = CreateDbContext();
        var user = CreateUser(1001);
        var repository = CreateRepository();
        var baseRun = CreateRun(repository, 12, "base", user);
        var targetRun = CreateRun(repository, 13, "target", user);
        dbContext.AddRange(user, repository, baseRun, targetRun);
        await dbContext.SaveChangesAsync();

        var result = await new ReviewComparisonService(dbContext).CompareAsync(
            user.GitHubUserId,
            baseRun.Id.ToString("N"),
            targetRun.Id.ToString("N"),
            CancellationToken.None);

        Assert.Equal(ReviewComparisonResultStatus.Invalid, result.Status);
        Assert.Equal("reviews_not_same_pull_request", result.ErrorCode);
    }

    [Fact]
    public async Task CompareAsync_DoesNotExposeAnotherUsersReview()
    {
        await using var dbContext = CreateDbContext();
        var owner = CreateUser(1001);
        var otherUser = CreateUser(2002);
        var repository = CreateRepository();
        var baseRun = CreateRun(repository, 12, "base", owner);
        var targetRun = CreateRun(repository, 12, "target", otherUser);
        dbContext.AddRange(owner, otherUser, repository, baseRun, targetRun);
        await dbContext.SaveChangesAsync();

        var result = await new ReviewComparisonService(dbContext).CompareAsync(
            owner.GitHubUserId,
            baseRun.Id.ToString("N"),
            targetRun.Id.ToString("N"),
            CancellationToken.None);

        Assert.Equal(ReviewComparisonResultStatus.NotFound, result.Status);
        Assert.Equal("review_not_found", result.ErrorCode);
    }

    [Fact]
    public async Task CompareAsync_AcceptsCompactReviewIds()
    {
        await using var dbContext = CreateDbContext();
        var user = CreateUser(1001);
        var repository = CreateRepository();
        var baseRun = CreateRun(repository, 12, "base", user);
        var targetRun = CreateRun(repository, 12, "target", user);
        dbContext.AddRange(user, repository, baseRun, targetRun);
        await dbContext.SaveChangesAsync();

        var result = await new ReviewComparisonService(dbContext).CompareAsync(
            user.GitHubUserId,
            baseRun.Id.ToString("N"),
            targetRun.Id.ToString("N"),
            CancellationToken.None);

        Assert.Equal(ReviewComparisonResultStatus.Success, result.Status);
    }

    [Fact]
    public async Task CompareAsync_RejectsInvalidReviewIds()
    {
        await using var dbContext = CreateDbContext();

        var result = await new ReviewComparisonService(dbContext).CompareAsync(
            1001,
            "not-a-review-id",
            Guid.NewGuid().ToString("N"),
            CancellationToken.None);

        Assert.Equal(ReviewComparisonResultStatus.Invalid, result.Status);
        Assert.Equal("invalid_review_id", result.ErrorCode);
    }

    private static PullSightDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PullSightDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new PullSightDbContext(options);
    }

    private static AppUser CreateUser(long githubUserId)
    {
        return new AppUser
        {
            GitHubUserId = githubUserId,
            Login = $"user-{githubUserId}",
        };
    }

    private static RepositoryRecord CreateRepository()
    {
        return new RepositoryRecord
        {
            GitHubRepositoryId = 99,
            Owner = "owner",
            Name = "repo",
            FullName = "owner/repo",
        };
    }

    private static ReviewRun CreateRun(
        RepositoryRecord repository,
        int pullRequestNumber,
        string headSha,
        AppUser? user = null)
    {
        return new ReviewRun
        {
            UserId = user?.Id ?? Guid.NewGuid(),
            RepositoryId = repository.Id,
            Repository = repository,
            PullRequestNumber = pullRequestNumber,
            HeadSha = headSha,
            Analyzer = "test",
            Source = "rule",
            Status = "completed",
            Summary = "Test review",
        };
    }

    private static ReviewFinding CreateFinding(int line)
    {
        return new ReviewFinding
        {
            Severity = "high",
            FilePath = @"src\Auth.cs",
            LineNumber = line,
            Title = "Unsafe token comparison",
            RuleId = "security-token",
            Message = "Use a constant-time comparison.",
        };
    }
}
