namespace PullSight.Api.Contracts.Reviews;

public sealed record ReviewHistoryQuery(
    int Page = 1,
    int PageSize = 10,
    string? Repository = null,
    int? PullRequestNumber = null,
    string? HeadSha = null,
    string? Source = null,
    string? Status = null);
