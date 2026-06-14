using System.Text;
using System.Text.Json;
using PullSight.Api.Contracts.Reviews;
using PullSight.Api.Data.Entities;
using PullSight.Api.Services.ReviewAnalysis;
using Xunit;

namespace PullSight.Api.Tests;

public sealed class ReviewReportServiceTests
{
    [Fact]
    public async Task ExportReviewAsync_ReturnsMarkdown()
    {
        var setup = await CreateSetupAsync();
        var result = await setup.Service.ExportReviewAsync(
            setup.User.GitHubUserId,
            setup.BaseRun.Id,
            "markdown",
            CancellationToken.None);

        var markdown = Encoding.UTF8.GetString(result.Content!);
        Assert.Contains("# PullSight Review Report", markdown);
        Assert.Contains("Unsafe token comparison", markdown);
        Assert.Equal("text/markdown; charset=utf-8", result.ContentType);
    }

    [Fact]
    public async Task ExportReviewAsync_ReturnsJson()
    {
        var setup = await CreateSetupAsync();
        var result = await setup.Service.ExportReviewAsync(
            setup.User.GitHubUserId,
            setup.BaseRun.Id,
            "json",
            CancellationToken.None);

        using var json = JsonDocument.Parse(result.Content!);
        Assert.Equal("owner/repo", json.RootElement.GetProperty("repositoryFullName").GetString());
        Assert.Equal("application/json; charset=utf-8", result.ContentType);
    }

    [Fact]
    public async Task ExportComparisonAsync_ReturnsComparisonReport()
    {
        var setup = await CreateSetupAsync();
        var result = await setup.Service.ExportComparisonAsync(
            setup.User.GitHubUserId,
            new ReviewComparisonExportRequest(
                setup.BaseRun.Id.ToString("N"),
                setup.TargetRun.Id.ToString("N"),
                "markdown"),
            CancellationToken.None);

        var markdown = Encoding.UTF8.GetString(result.Content!);
        Assert.Contains("# PullSight Comparison Report", markdown);
        Assert.Contains("## Added (1)", markdown);
    }

    [Fact]
    public async Task ExportReviewAsync_EnforcesOwnership()
    {
        var setup = await CreateSetupAsync();
        var result = await setup.Service.ExportReviewAsync(
            9999,
            setup.BaseRun.Id,
            "markdown",
            CancellationToken.None);

        Assert.Equal("review_not_found", result.ErrorCode);
    }

    [Fact]
    public async Task ExportReviewAsync_RejectsInvalidFormat()
    {
        var setup = await CreateSetupAsync();
        var result = await setup.Service.ExportReviewAsync(
            setup.User.GitHubUserId,
            setup.BaseRun.Id,
            "pdf",
            CancellationToken.None);

        Assert.Equal("invalid_export_format", result.ErrorCode);
    }

    private static async Task<ReportSetup> CreateSetupAsync()
    {
        var db = TestData.CreateDbContext();
        var user = TestData.CreateUser(1001);
        var repository = TestData.CreateRepository();
        var baseRun = TestData.CreateRun(user, repository, headSha: "base123456");
        var targetRun = TestData.CreateRun(user, repository, headSha: "target123456");
        baseRun.Findings.Add(TestData.CreateFinding(line: 10));
        targetRun.Findings.Add(TestData.CreateFinding(line: 11));
        db.AddRange(user, repository, baseRun, targetRun);
        await db.SaveChangesAsync();

        var history = new ReviewHistoryService(db);
        return new ReportSetup(
            user,
            baseRun,
            targetRun,
            new ReviewReportService(history, new ReviewComparisonService(db)));
    }

    private sealed record ReportSetup(
        AppUser User,
        ReviewRun BaseRun,
        ReviewRun TargetRun,
        ReviewReportService Service);
}
