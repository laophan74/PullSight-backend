using Microsoft.AspNetCore.Mvc;

namespace PullSight.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "healthy",
            service = "PullSight.Api",
            utc = DateTimeOffset.UtcNow,
        });
    }
}
