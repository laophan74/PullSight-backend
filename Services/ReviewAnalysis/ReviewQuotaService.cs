using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PullSight.Api.Data;
using PullSight.Api.Data.Entities;

namespace PullSight.Api.Services.ReviewAnalysis;

public sealed class ReviewQuotaService(
    PullSightDbContext dbContext,
    IOptions<GeminiOptions> geminiOptions)
{
    private readonly GeminiOptions options = geminiOptions.Value;

    public async Task<QuotaReservation> TryReserveGeminiReviewAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var dailyLimit = Math.Max(0, options.DailyLimit);
        var usageDate = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var usage = await dbContext.UsageLimits.FirstOrDefaultAsync(
            limit => limit.UserId == userId && limit.UsageDate == usageDate,
            cancellationToken);

        if (usage is null)
        {
            usage = new UsageLimit
            {
                UserId = userId,
                UsageDate = usageDate,
                DailyLimit = dailyLimit,
            };
            dbContext.UsageLimits.Add(usage);
        }
        else
        {
            usage.DailyLimit = dailyLimit;
        }

        if (usage.ReviewCount >= dailyLimit)
        {
            return new QuotaReservation(false, 0, dailyLimit);
        }

        usage.ReviewCount++;
        usage.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new QuotaReservation(true, Math.Max(0, dailyLimit - usage.ReviewCount), dailyLimit);
    }

    public async Task<int> GetGeminiReviewsRemainingAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var dailyLimit = Math.Max(0, options.DailyLimit);
        var usageDate = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var reviewCount = await dbContext.UsageLimits
            .Where(limit => limit.UserId == userId && limit.UsageDate == usageDate)
            .Select(limit => limit.ReviewCount)
            .FirstOrDefaultAsync(cancellationToken);

        return Math.Max(0, dailyLimit - reviewCount);
    }
}

public sealed record QuotaReservation(
    bool WasReserved,
    int Remaining,
    int DailyLimit);
