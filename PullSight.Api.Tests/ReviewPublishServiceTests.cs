using System.Net;
using PullSight.Api.Contracts.Reviews;
using PullSight.Api.Data.Entities;
using PullSight.Api.Services.GitHub;
using PullSight.Api.Services.ReviewAnalysis;
using Xunit;

namespace PullSight.Api.Tests;

public sealed class ReviewPublishServiceTests
{
    [Fact]
    public async Task PublishReviewAsync_DoesNotPublishAnotherUsersReview()
    {
        var setup = await CreateSetupAsync();
        var result = await setup.Service.PublishReviewAsync(
            9999,
            setup.BaseRun.Id,
            "token",
            CancellationToken.None);

        Assert.Equal("review_not_found", result.ErrorCode);
        Assert.Equal(0, setup.Handler.CallCount);
    }

    [Fact]
    public async Task PublishComparisonAsync_RejectsDifferentPullRequests()
    {
        var setup = await CreateSetupAsync(targetPullRequestNumber: 13);
        var result = await setup.Service.PublishComparisonAsync(
            setup.User.GitHubUserId,
            new ReviewComparisonPublishRequest(
                setup.BaseRun.Id.ToString("N"),
                setup.TargetRun.Id.ToString("N")),
            "token",
            CancellationToken.None);

        Assert.Equal("reviews_not_same_pull_request", result.ErrorCode);
        Assert.Equal(0, setup.Handler.CallCount);
    }

    [Fact]
    public async Task PublishReviewAsync_RejectsMissingGitHubToken()
    {
        var setup = await CreateSetupAsync();
        var result = await setup.Service.PublishReviewAsync(
            setup.User.GitHubUserId,
            setup.BaseRun.Id,
            "",
            CancellationToken.None);

        Assert.Equal("github_token_missing", result.ErrorCode);
        Assert.Equal(0, setup.Handler.CallCount);
    }

    [Fact]
    public async Task PublishReviewAsync_MapsGitHubApiFailure()
    {
        var setup = await CreateSetupAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var result = await setup.Service.PublishReviewAsync(
            setup.User.GitHubUserId,
            setup.BaseRun.Id,
            "token",
            CancellationToken.None);

        Assert.Equal("github_comment_failed", result.ErrorCode);
    }

    private static async Task<PublishSetup> CreateSetupAsync(
        HttpResponseMessage? response = null,
        int targetPullRequestNumber = 12)
    {
        var db = TestData.CreateDbContext();
        var user = TestData.CreateUser(1001);
        var repository = TestData.CreateRepository();
        var baseRun = TestData.CreateRun(user, repository, 12, "base123");
        var targetRun = TestData.CreateRun(user, repository, targetPullRequestNumber, "target123");
        baseRun.Findings.Add(TestData.CreateFinding());
        db.AddRange(user, repository, baseRun, targetRun);
        await db.SaveChangesAsync();

        var handler = new CountingHandler(response ?? new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var history = new ReviewHistoryService(db);
        var service = new ReviewPublishService(
            history,
            new ReviewComparisonService(db),
            new GitHubCommentService(new HttpClient(handler)));

        return new PublishSetup(user, baseRun, targetRun, service, handler);
    }

    private sealed record PublishSetup(
        AppUser User,
        ReviewRun BaseRun,
        ReviewRun TargetRun,
        ReviewPublishService Service,
        CountingHandler Handler);

    private sealed class CountingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(response);
        }
    }
}
