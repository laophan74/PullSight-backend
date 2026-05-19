using Microsoft.AspNetCore.Mvc;
using PullSight.Api.Contracts.Reviews;
using PullSight.Api.Services.ReviewAnalysis;

namespace PullSight.Api.Controllers;

[ApiController]
[Route("api/reviews")]
public sealed class ReviewsController(ICodeReviewAnalyzer analyzer) : ControllerBase
{
    [HttpPost("demo")]
    public async Task<ActionResult<ReviewRunResponse>> AnalyzeDemo(CancellationToken cancellationToken)
    {
        var result = await analyzer.AnalyzeDemoAsync(cancellationToken);

        return Ok(result);
    }
}
