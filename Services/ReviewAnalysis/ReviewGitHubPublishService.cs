using System.Security.Cryptography;
using System.Text;
using PullSight.Api.Contracts.Reviews;
using PullSight.Api.Services.GitHub;

namespace PullSight.Api.Services.ReviewAnalysis;

public sealed class ReviewGitHubPublishService(
    ReviewHistoryService reviewHistoryService,
    GitHubApiService gitHubApiService,
    GitHubCheckService gitHubCheckService,
    GitHubInlineCommentService gitHubInlineCommentService)
{
    public async Task<CheckRunPublishResult> PublishCheckRunAsync(
        long githubUserId,
        Guid reviewRunId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return CheckRunPublishResult.Invalid("github_token_missing", "A GitHub access token is required.");
        }

        var review = await reviewHistoryService.GetReviewAsync(
            githubUserId,
            reviewRunId,
            cancellationToken);
        if (review is null)
        {
            return CheckRunPublishResult.Invalid("review_not_found", "The review run was not found.");
        }

        if (!ReviewRunPolicy.IsCompleted(review.Status))
        {
            return CheckRunPublishResult.Invalid(
                "review_not_completed",
                "Only completed or fallback reviews can be published as a check run.");
        }

        var context = SplitRepository(review.RepositoryFullName);
        if (context is null)
        {
            return CheckRunPublishResult.Invalid(
                "github_repository_unavailable",
                "The persisted repository is invalid.");
        }

        var diff = await GetDiffAsync(context.Value, review.PullRequestNumber, accessToken, cancellationToken);
        if (diff is null)
        {
            return CheckRunPublishResult.Invalid(
                "github_repository_unavailable",
                "The GitHub repository or pull request is unavailable.");
        }

        if (!string.Equals(diff.HeadSha, review.HeadSha, StringComparison.OrdinalIgnoreCase))
        {
            return CheckRunPublishResult.Invalid(
                "review_head_outdated",
                "The pull request head changed after this review was created.");
        }

        var annotations = review.Findings
            .Where(finding => finding.Severity is "critical" or "high" or "medium")
            .Where(finding => finding.IsInlineCommentable)
            .Where(finding => GitHubDiffLineMapper.IsChangedRightSideLine(
                diff,
                finding.FilePath,
                finding.Line))
            .Take(GitHubCheckService.MaxAnnotations)
            .Select(finding => new GitHubCheckAnnotation(
                finding.FilePath,
                finding.Line,
                finding.Severity is "critical" or "high" ? "failure" : "warning",
                finding.Title,
                BuildAnnotationMessage(finding)))
            .ToList();
        var conclusion = GetConclusion(review.Findings);
        var result = await gitHubCheckService.UpsertAsync(
            review.RepositoryFullName,
            review.HeadSha,
            $"pullsight:{review.Id}",
            conclusion,
            $"PullSight review: risk {review.RiskScore}",
            BuildCheckSummary(review),
            annotations,
            accessToken,
            cancellationToken);

        return result.IsSuccess
            ? CheckRunPublishResult.Success(new CheckRunPublishResponse(
                result.Status!,
                result.CheckRunId!.Value,
                result.CheckRunUrl!,
                conclusion,
                annotations.Count))
            : CheckRunPublishResult.Invalid(result.ErrorCode!, result.ErrorMessage!);
    }

    public async Task<InlineCommentsPublishResult> PublishInlineCommentsAsync(
        long githubUserId,
        Guid reviewRunId,
        InlineCommentsPublishRequest request,
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return InlineCommentsPublishResult.Invalid("github_token_missing", "A GitHub access token is required.");
        }

        var review = await reviewHistoryService.GetReviewAsync(
            githubUserId,
            reviewRunId,
            cancellationToken);
        if (review is null)
        {
            return InlineCommentsPublishResult.Invalid("review_not_found", "The review run was not found.");
        }

        if (!ReviewRunPolicy.IsCompleted(review.Status))
        {
            return InlineCommentsPublishResult.Invalid(
                "review_not_completed",
                "Only completed or fallback reviews can publish inline comments.");
        }

        var selectedIds = request.FindingIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedIds is { Count: > 0 }
            && selectedIds.Any(id => review.Findings.All(finding =>
                !string.Equals(finding.Id, id, StringComparison.OrdinalIgnoreCase))))
        {
            return InlineCommentsPublishResult.Invalid(
                "finding_not_found",
                "One or more findings do not belong to this review.");
        }

        var context = SplitRepository(review.RepositoryFullName);
        if (context is null)
        {
            return InlineCommentsPublishResult.Invalid(
                "github_repository_unavailable",
                "The persisted repository is invalid.");
        }

        var diff = await GetDiffAsync(context.Value, review.PullRequestNumber, accessToken, cancellationToken);
        if (diff is null)
        {
            return InlineCommentsPublishResult.Invalid(
                "github_repository_unavailable",
                "The GitHub repository or pull request is unavailable.");
        }

        if (!string.Equals(diff.HeadSha, review.HeadSha, StringComparison.OrdinalIgnoreCase))
        {
            return InlineCommentsPublishResult.Invalid(
                "review_head_outdated",
                "The pull request head changed after this review was created.");
        }

        var findings = review.Findings.AsEnumerable();
        if (selectedIds is { Count: > 0 })
        {
            findings = findings.Where(finding => selectedIds.Contains(finding.Id));
        }
        else if (request.ImportantOnly)
        {
            findings = findings.Where(finding => finding.Severity is "critical" or "high");
        }

        var items = new List<InlineCommentPublishItemResponse>();
        foreach (var finding in findings)
        {
            if (!finding.IsInlineCommentable
                || !GitHubDiffLineMapper.IsChangedRightSideLine(diff, finding.FilePath, finding.Line))
            {
                items.Add(new InlineCommentPublishItemResponse(
                    finding.Id,
                    "skipped",
                    null,
                    null,
                    "finding_not_inline_commentable",
                    "The finding does not map to an added line in the current pull request diff."));
                continue;
            }

            var identity = StableFindingIdentity(finding);
            var marker = $"<!-- pullsight-inline:{review.Id}:{identity} -->";
            var result = await gitHubInlineCommentService.UpsertAsync(
                review.RepositoryFullName,
                review.PullRequestNumber,
                review.HeadSha,
                finding.FilePath,
                finding.Line,
                marker,
                BuildInlineComment(finding, marker),
                accessToken,
                cancellationToken);
            items.Add(new InlineCommentPublishItemResponse(
                finding.Id,
                result.Status,
                result.CommentId,
                result.CommentUrl,
                result.ErrorCode,
                result.ErrorMessage));
        }

        return InlineCommentsPublishResult.Success(new InlineCommentsPublishResponse(
            items,
            items.Count(item => item.Status == "created"),
            items.Count(item => item.Status == "updated"),
            items.Count(item => item.Status == "alreadyPublished"),
            items.Count(item => item.Status == "skipped"),
            items.Count(item => item.Status == "failed")));
    }

    internal static string GetConclusion(IReadOnlyList<ReviewFindingResponse> findings)
    {
        if (findings.Any(finding => finding.Severity is "critical" or "high"))
        {
            return "failure";
        }

        return findings.Any(finding => finding.Severity == "medium")
            ? "neutral"
            : "success";
    }

    internal static string StableFindingIdentity(ReviewFindingResponse finding)
    {
        var normalized = string.Join(
            '\u001f',
            finding.Severity.Trim().ToLowerInvariant(),
            finding.FilePath.Trim().Replace('\\', '/').ToLowerInvariant(),
            finding.Line,
            finding.Title.Trim().ToLowerInvariant(),
            finding.Detail.Trim().ToLowerInvariant(),
            finding.Source.Trim().ToLowerInvariant());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))
            .ToLowerInvariant()[..20];
    }

    private async Task<PullSight.Api.Contracts.GitHub.GitHubPullRequestDiffResponse?> GetDiffAsync(
        (string Owner, string Name) repository,
        int pullRequestNumber,
        string accessToken,
        CancellationToken cancellationToken)
    {
        try
        {
            return await gitHubApiService.GetPullRequestDiffAsync(
                repository.Owner,
                repository.Name,
                pullRequestNumber,
                accessToken,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    private static string BuildCheckSummary(ReviewHistoryDetailResponse review)
    {
        var counts = string.Join(
            ", ",
            review.Findings
                .GroupBy(finding => finding.Severity)
                .OrderBy(group => SeverityRank(group.Key))
                .Select(group => $"{group.Key}: {group.Count()}"));
        var tests = string.Join(
            "\n",
            review.SummaryDetails.SuggestedTestPlan.Select(test => $"- {test}"));

        return $"""
            {review.SummaryDetails.Overview}

            **Risk overview:** {review.SummaryDetails.RiskOverview}

            **Risk score:** {review.RiskScore}
            **Analyzer:** {review.Analyzer}
            **Head SHA:** `{review.HeadSha}`
            **Findings:** {(counts.Length == 0 ? "none" : counts)}

            ### Suggested test plan
            {tests}
            """;
    }

    private static string BuildAnnotationMessage(ReviewFindingResponse finding) =>
        string.IsNullOrWhiteSpace(finding.Suggestion)
            ? $"{finding.Detail}\n\nSource: {finding.Source}"
            : $"{finding.Detail}\n\nSuggested fix: {finding.Suggestion}\n\nSource: {finding.Source}";

    private static string BuildInlineComment(ReviewFindingResponse finding, string marker)
    {
        var suggestion = string.IsNullOrWhiteSpace(finding.Suggestion)
            ? string.Empty
            : $"\n\n**Suggested fix:** {finding.Suggestion}";
        return $"""
            **{finding.Severity.ToUpperInvariant()} - {finding.Title}**

            {finding.Detail}

            **Source:** `{finding.Source}`{suggestion}

            {marker}
            """;
    }

    private static (string Owner, string Name)? SplitRepository(string fullName)
    {
        var parts = fullName.Split('/', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2 ? (parts[0], parts[1]) : null;
    }

    private static int SeverityRank(string severity) => severity switch
    {
        "critical" => 0,
        "high" => 1,
        "medium" => 2,
        _ => 3,
    };
}

public sealed record CheckRunPublishResult(
    CheckRunPublishResponse? Response,
    string? ErrorCode,
    string? ErrorMessage)
{
    public bool IsSuccess => Response is not null;

    public static CheckRunPublishResult Success(CheckRunPublishResponse response) =>
        new(response, null, null);

    public static CheckRunPublishResult Invalid(string code, string message) =>
        new(null, code, message);
}

public sealed record InlineCommentsPublishResult(
    InlineCommentsPublishResponse? Response,
    string? ErrorCode,
    string? ErrorMessage)
{
    public bool IsSuccess => Response is not null;

    public static InlineCommentsPublishResult Success(InlineCommentsPublishResponse response) =>
        new(response, null, null);

    public static InlineCommentsPublishResult Invalid(string code, string message) =>
        new(null, code, message);
}
