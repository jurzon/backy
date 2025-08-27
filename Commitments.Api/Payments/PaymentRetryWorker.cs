using Commitments.Domain.Entities;
using Commitments.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Commitments.Api.Payments;

public interface IPaymentRetryWorker
{
    Task RunAsync(CancellationToken ct = default);
}

public class PaymentRetryWorker(AppDbContext db, IPaymentService paymentService, ILogger<PaymentRetryWorker> logger) : IPaymentRetryWorker
{
    private static readonly HashSet<string> HardDeclineCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "card_declined","do_not_honor","lost_card","stolen_card","fraudulent"
    };

    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        // Retry once per day: select failed intents with attempt <5 and not hard decline, last update >24h ago
        var candidates = await db.PaymentIntentLogs
            .Where(p => p.Status == "failed" && p.AttemptNumber < 5 && (p.LastErrorCode == null || !HardDeclineCodes.Contains(p.LastErrorCode))
                && (now - p.UpdatedAtUtc) > TimeSpan.FromHours(24))
            .ToListAsync(ct);

        foreach (var log in candidates)
        {
            try
            {
                log.AttemptNumber += 1;
                // Simulate retry success (in real impl we'd call Stripe to confirm)
                log.Status = "succeeded";
                log.LastErrorCode = null;
                log.UpdatedAtUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Retry for PaymentIntent {Id} failed", log.StripePaymentIntentId);
            }
        }
        if (candidates.Count > 0)
            await db.SaveChangesAsync(ct);
    }
}
