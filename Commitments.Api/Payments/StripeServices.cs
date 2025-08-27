using Commitments.Domain.Entities;
using Commitments.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Stripe;

namespace Commitments.Api.Payments;

public interface IPaymentService
{
    Task EnsureSetupIntentAsync(Guid userId, CancellationToken ct = default);
    Task<PaymentIntentLog> CreateFailurePaymentIntentAsync(Commitment commitment, CancellationToken ct = default);
    Task UpdatePaymentStatusAsync(string paymentIntentId, string newStatus, string? errorCode = null, CancellationToken ct = default);
}

public class StripePaymentService : IPaymentService
{
    private readonly PaymentIntentService _pi;
    private readonly SetupIntentService _si;
    private readonly AppDbContext _db;
    private readonly ILogger<StripePaymentService> _logger;
    private readonly bool _stripeConfigured;

    public StripePaymentService(AppDbContext db, ILogger<StripePaymentService> logger)
    {
        _db = db;
        _logger = logger;
        _pi = new PaymentIntentService();
        _si = new SetupIntentService();
        _stripeConfigured = !string.IsNullOrWhiteSpace(Stripe.StripeConfiguration.ApiKey);
    }

    public async Task EnsureSetupIntentAsync(Guid userId, CancellationToken ct = default)
    {
        // Placeholder: would create SetupIntent if user has no payment method.
        await Task.CompletedTask;
    }

    public async Task<PaymentIntentLog> CreateFailurePaymentIntentAsync(Commitment commitment, CancellationToken ct = default)
    {
        // Idempotency: look for existing PaymentIntentLog for this commitment in created/processing state
        var existing = await _db.PaymentIntentLogs.FirstOrDefaultAsync(p => p.CommitmentId == commitment.Id && p.Status != "succeeded", ct);
        if (existing != null) return existing;

        var idempotencyKey = $"fail-{commitment.Id}"; // TODO sequence
        string paymentIntentId;
        string status = "created";
        if (_stripeConfigured)
        {
            try
            {
                var req = new PaymentIntentCreateOptions
                {
                    Amount = commitment.StakeAmountMinor,
                    Currency = commitment.Currency.ToLowerInvariant(),
                    Metadata = new Dictionary<string, string>{{"commitment_id", commitment.Id.ToString()}}
                };
                var pi = await _pi.CreateAsync(req, new RequestOptions { IdempotencyKey = idempotencyKey }, ct);
                paymentIntentId = pi.Id;
                status = pi.Status ?? "created";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stripe PaymentIntent creation failed for {CommitmentId}", commitment.Id);
                paymentIntentId = idempotencyKey + "-sim"; // fallback
                status = "failed";
            }
        }
        else
        {
            // Simulated ID in absence of API key
            paymentIntentId = idempotencyKey + "-local";
        }

        var log = new PaymentIntentLog
        {
            CommitmentId = commitment.Id,
            StripePaymentIntentId = paymentIntentId,
            AmountMinor = commitment.StakeAmountMinor,
            Currency = commitment.Currency,
            Status = status,
            AttemptNumber = 1
        };
        _db.PaymentIntentLogs.Add(log);
        await _db.SaveChangesAsync(ct);
        return log;
    }

    public async Task UpdatePaymentStatusAsync(string paymentIntentId, string newStatus, string? errorCode = null, CancellationToken ct = default)
    {
        var log = await _db.PaymentIntentLogs.FirstOrDefaultAsync(p => p.StripePaymentIntentId == paymentIntentId, ct);
        if (log == null)
        {
            _logger.LogWarning("Webhook for unknown payment intent {Id}", paymentIntentId);
            return;
        }
        log.Status = newStatus;
        log.LastErrorCode = errorCode;
        log.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}
