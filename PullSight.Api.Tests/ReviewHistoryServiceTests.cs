using PullSight.Api.Contracts.Reviews;
using PullSight.Api.Services.ReviewAnalysis;
using Xunit;

namespace PullSight.Api.Tests;

public sealed class ReviewHistoryServiceTests
{
    [Fact]
    public async Task GetReviewsAsync_OnlyReturnsCurrentUsersReviews()
    {
        await using var db = TestData.CreateDbContext();
        var owner = TestData.CreateUser(1001);
        var other = TestData.CreateUser(2002);
        var repository = TestData.CreateRepository();
        db.AddRange(
            owner,
            other,
            repository,
            TestData.CreateRun(owner, repository, headSha: "owner"),
            TestData.CreateRun(other, repository, headSha: "other"));
        await db.SaveChangesAsync();

        var result = await new ReviewHistoryService(db).GetReviewsAsync(
            owner.GitHubUserId,
            new ReviewHistoryQuery(),
            CancellationToken.None);

        var item = Assert.Single(result.Page!.Items);
        Assert.Equal("owner", item.HeadSha);
    }

    [Fact]
    public async Task GetReviewsAsync_FiltersByRepository()
    {
        var result = await RunFilterTest(new ReviewHistoryQuery(Repository: "owner/first"));
        Assert.Equal("owner/first", Assert.Single(result.Page!.Items).RepositoryFullName);
    }

    [Fact]
    public async Task GetReviewsAsync_FiltersByPullRequestNumber()
    {
        var result = await RunFilterTest(new ReviewHistoryQuery(PullRequestNumber: 12));
        Assert.Equal(12, Assert.Single(result.Page!.Items).PullRequestNumber);
    }

    [Fact]
    public async Task GetReviewsAsync_FiltersByHeadShaPrefix()
    {
        var result = await RunFilterTest(new ReviewHistoryQuery(HeadSha: "aaa"));
        Assert.Equal("aaa111", Assert.Single(result.Page!.Items).HeadSha);
    }

    [Fact]
    public async Task GetReviewsAsync_FiltersBySource()
    {
        var result = await RunFilterTest(new ReviewHistoryQuery(Source: "rule"));
        Assert.Equal("rule", Assert.Single(result.Page!.Items).Source);
    }

    [Fact]
    public async Task GetReviewsAsync_FiltersByStatus()
    {
        var result = await RunFilterTest(new ReviewHistoryQuery(Status: "fallback"));
        Assert.Equal("fallback", Assert.Single(result.Page!.Items).Status);
    }

    [Fact]
    public async Task GetReviewsAsync_CombinesFilters()
    {
        var result = await RunFilterTest(new ReviewHistoryQuery(
            Repository: "owner/first",
            PullRequestNumber: 12,
            HeadSha: "aaa",
            Source: "ai",
            Status: "completed"));

        Assert.Single(result.Page!.Items);
    }

    [Fact]
    public async Task GetReviewsAsync_PaginatesAfterFiltering()
    {
        await using var db = TestData.CreateDbContext();
        var user = TestData.CreateUser(1001);
        var repository = TestData.CreateRepository();
        db.AddRange(user, repository);
        for (var index = 0; index < 5; index++)
        {
            db.Add(TestData.CreateRun(
                user,
                repository,
                pullRequestNumber: 12,
                headSha: $"sha-{index}",
                createdAt: DateTimeOffset.UtcNow.AddMinutes(index)));
        }

        db.Add(TestData.CreateRun(user, repository, pullRequestNumber: 99, headSha: "excluded"));
        await db.SaveChangesAsync();

        var result = await new ReviewHistoryService(db).GetReviewsAsync(
            user.GitHubUserId,
            new ReviewHistoryQuery(Page: 2, PageSize: 2, PullRequestNumber: 12),
            CancellationToken.None);

        Assert.Equal(5, result.Page!.TotalCount);
        Assert.Equal(3, result.Page.TotalPages);
        Assert.Equal(2, result.Page.Items.Count);
        Assert.DoesNotContain(result.Page.Items, item => item.PullRequestNumber == 99);
    }

    [Fact]
    public async Task GetReviewsAsync_RejectsInvalidPullRequestNumber()
    {
        await using var db = TestData.CreateDbContext();
        var result = await new ReviewHistoryService(db).GetReviewsAsync(
            1001,
            new ReviewHistoryQuery(PullRequestNumber: 0),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("invalid_pull_request_number", result.ErrorCode);
    }

    private static async Task<ReviewHistoryQueryResult> RunFilterTest(ReviewHistoryQuery query)
    {
        await using var db = TestData.CreateDbContext();
        var user = TestData.CreateUser(1001);
        var first = TestData.CreateRepository("owner/first", 11);
        var second = TestData.CreateRepository("owner/second", 22);
        db.AddRange(
            user,
            first,
            second,
            TestData.CreateRun(user, first, 12, "aaa111", "ai", "completed"),
            TestData.CreateRun(user, second, 13, "bbb222", "rule", "fallback"));
        await db.SaveChangesAsync();

        return await new ReviewHistoryService(db).GetReviewsAsync(
            user.GitHubUserId,
            query,
            CancellationToken.None);
    }
}
