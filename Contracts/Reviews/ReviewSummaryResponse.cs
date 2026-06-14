namespace PullSight.Api.Contracts.Reviews;

public sealed record ReviewSummaryResponse(
    string Overview,
    string RiskOverview,
    IReadOnlyList<string> KeyChanges,
    IReadOnlyList<string> SuggestedTestPlan);

