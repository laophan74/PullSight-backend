using PullSight.Api.Contracts.GitHub;
using PullSight.Api.Contracts.Reviews;
using PullSight.Api.Services.ReviewAnalysis;
using Xunit;

namespace PullSight.Api.Tests;

public sealed class ReviewLifecycleAndSummaryTests
{
    [Fact]
    public async Task Persistence_TracksQueuedAnalyzingCompletedLifecycle()
    {
        await using var db = TestData.CreateDbContext();
        var user = TestData.CreateUser(1001);
        var repository = TestData.CreateRepository();
        db.AddRange(user, repository);
        await db.SaveChangesAsync();
        var service = new ReviewPersistenceService(db);
        var diff = CreateDiff();

        var queued = await service.CreateQueuedReviewAsync(
            user.Id,
            repository.Id,
            repository.FullName,
            diff,
            CancellationToken.None);
        Assert.Equal("queued", queued.Status);

        await service.SetAnalyzingAsync(queued.Id, CancellationToken.None);
        Assert.Equal("analyzing", db.ReviewRuns.Single().Status);

        var summary = new ReviewSummaryResponse(
            "Overview",
            "Low risk",
            ["Changed diagnostics."],
            ["Run diagnostics."]);
        var completed = await service.CompleteReviewAsync(
            queued.Id,
            new ReviewRunResponse(
                "temporary",
                repository.FullName,
                diff.Number,
                diff.HeadSha,
                "completed",
                "test",
                10,
                0,
                DateTimeOffset.UtcNow,
                summary.Overview,
                summary,
                null,
                []),
            0,
            CancellationToken.None);

        Assert.Equal("completed", completed.Status);
        Assert.Equal("Overview", completed.SummaryDetails.Overview);
    }

    [Fact]
    public async Task Persistence_StoresOnlySanitizedFailedError()
    {
        await using var db = TestData.CreateDbContext();
        var user = TestData.CreateUser(1001);
        var repository = TestData.CreateRepository();
        db.AddRange(user, repository);
        await db.SaveChangesAsync();
        var service = new ReviewPersistenceService(db);
        var queued = await service.CreateQueuedReviewAsync(
            user.Id,
            repository.Id,
            repository.FullName,
            CreateDiff(),
            CancellationToken.None);
        var raw = new InvalidOperationException("token=secret-value backend-config");

        await service.MarkFailedAsync(
            queued.Id,
            ReviewRunPolicy.SanitizeError(raw),
            CancellationToken.None);

        var failed = db.ReviewRuns.Single();
        Assert.Equal("failed", failed.Status);
        Assert.DoesNotContain("secret-value", failed.ErrorMessage);
        Assert.Equal("The review response could not be processed.", failed.ErrorMessage);
    }

    [Fact]
    public void SummaryService_PreservesLegacySummaryAndBoundsLists()
    {
        var service = new ReviewSummaryService();
        var summary = service.Normalize(
            new ReviewSummaryResponse(
                new string('a', 2_000),
                string.Empty,
                Enumerable.Range(1, 20).Select(index => $"Change {index}").ToList(),
                []),
            "Legacy summary");

        Assert.Equal(1_000, summary.Overview.Length);
        Assert.Equal(8, summary.KeyChanges.Count);
        Assert.NotEmpty(summary.RiskOverview);
        Assert.NotEmpty(summary.SuggestedTestPlan);

        var oldReview = service.FromStored(null, "Old review summary");
        Assert.Equal("Old review summary", oldReview.Overview);
    }

    private static GitHubPullRequestDiffResponse CreateDiff() => new(
        99,
        "owner/repo",
        100,
        12,
        "Test PR",
        "abcdef123456",
        1,
        1,
        0,
        [
            new GitHubPullRequestFileResponse(
                "file-sha",
                "src/Test.cs",
                "modified",
                1,
                0,
                1,
                "@@ -1 +1 @@\n+new",
                "https://example.test/blob",
                "https://example.test/raw",
                null),
        ]);
}

