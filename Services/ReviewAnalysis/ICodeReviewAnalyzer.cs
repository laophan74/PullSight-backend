using PullSight.Api.Contracts.Reviews;

namespace PullSight.Api.Services.ReviewAnalysis;

public interface ICodeReviewAnalyzer
{
    Task<ReviewRunResponse> AnalyzeDemoAsync(CancellationToken cancellationToken);
}
