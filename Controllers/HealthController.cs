using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using PullSight.Api.Data;
using PullSight.Api.Data.Entities;

namespace PullSight.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController(PullSightDbContext dbContext) : ControllerBase
{
    private static readonly string[] RequiredTables =
    [
        "users",
        "github_connections",
        "repositories",
        "pull_requests",
        "review_runs",
        "review_findings",
        "usage_limits",
    ];

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

    [HttpGet("db/schema")]
    public async Task<IActionResult> GetDatabaseSchema(CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);

        try
        {
            var tables = new List<TableHealthResponse>();

            foreach (var table in RequiredTables)
            {
                tables.Add(await GetTableHealthAsync(table, cancellationToken));
            }

            return Ok(new
            {
                status = tables.All(table => table.Exists && table.Error is null) ? "healthy" : "unhealthy",
                utc = DateTimeOffset.UtcNow,
                tables,
            });
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    [HttpPost("db/review-write-test")]
    public async Task<IActionResult> TestReviewWrite(CancellationToken cancellationToken)
    {
        var userId = await dbContext.Users
            .Select(user => user.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (userId == Guid.Empty)
        {
            return Ok(new
            {
                status = "skipped",
                reason = "No user exists to satisfy review_runs.UserId foreign key.",
                utc = DateTimeOffset.UtcNow,
            });
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var now = DateTimeOffset.UtcNow;
            var repository = new RepositoryRecord
            {
                GitHubRepositoryId = -DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Owner = "diagnostics",
                Name = "write-test",
                FullName = "diagnostics/write-test",
                CreatedAt = now,
                UpdatedAt = now,
            };
            var pullRequest = new PullRequestRecord
            {
                RepositoryId = repository.Id,
                Number = 1,
                Title = "Diagnostics write test",
                HeadSha = Guid.NewGuid().ToString("N"),
                CreatedAt = now,
                UpdatedAt = now,
            };
            var reviewRun = new ReviewRun
            {
                UserId = userId,
                RepositoryId = repository.Id,
                PullRequestNumber = pullRequest.Number,
                HeadSha = pullRequest.HeadSha,
                Analyzer = "Diagnostics",
                Source = "rule",
                Status = "fallback",
                RiskScore = 1,
                Summary = "Rollback-only diagnostics review.",
                CreatedAt = now,
            };
            reviewRun.Findings.Add(new ReviewFinding
            {
                Severity = "low",
                Title = "Diagnostics finding",
                FilePath = "diagnostics.txt",
                LineNumber = 1,
                RuleId = "diagnostics",
                Message = "Rollback-only diagnostics finding.",
                CreatedAt = now,
            });

            dbContext.Repositories.Add(repository);
            dbContext.PullRequests.Add(pullRequest);
            dbContext.ReviewRuns.Add(reviewRun);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.RollbackAsync(cancellationToken);

            return Ok(new
            {
                status = "healthy",
                wrote = new
                {
                    repository = true,
                    pullRequest = true,
                    reviewRun = true,
                    reviewFinding = true,
                },
                rolledBack = true,
                utc = DateTimeOffset.UtcNow,
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None);

            return Ok(new
            {
                status = "unhealthy",
                error = exception.Message,
                innerError = exception.InnerException?.Message,
                exceptionType = exception.GetType().Name,
                utc = DateTimeOffset.UtcNow,
            });
        }
    }

    private async Task<TableHealthResponse> GetTableHealthAsync(
        string table,
        CancellationToken cancellationToken)
    {
        try
        {
            var exists = await ScalarAsync<bool>(
                $"select exists (select 1 from information_schema.tables where table_schema = 'public' and table_name = '{table}')",
                cancellationToken);

            if (!exists)
            {
                return new TableHealthResponse(table, false, null, null);
            }

            var rowCount = await ScalarAsync<long>($"select count(*) from public.{table}", cancellationToken);

            return new TableHealthResponse(table, true, rowCount, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new TableHealthResponse(table, false, null, exception.Message);
        }
    }

    private async Task<T> ScalarAsync<T>(string sql, CancellationToken cancellationToken)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null || value == DBNull.Value)
        {
            throw new InvalidOperationException("Database scalar query returned no value.");
        }

        return (T)Convert.ChangeType(value, typeof(T));
    }

    private sealed record TableHealthResponse(
        string Name,
        bool Exists,
        long? RowCount,
        string? Error);
}
