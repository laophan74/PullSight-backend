using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using PullSight.Api.Data;

namespace PullSight.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController(PullSightDbContext dbContext) : ControllerBase
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

    [HttpGet("db")]
    public async Task<IActionResult> GetDatabase(CancellationToken cancellationToken)
    {
        var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

        return Ok(new
        {
            status = canConnect ? "healthy" : "unhealthy",
            database = "postgres",
            provider = dbContext.Database.ProviderName,
            utc = DateTimeOffset.UtcNow,
        });
    }
}
