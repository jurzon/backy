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
        var state = await _db.PaymentSetupStates.FirstOrDefaultAsync(s => s.UserId == userId, ct);
        if (state != null && state.HasPaymentMethod) return; // already set

        string? setupIntentId = null;
        if (_stripeConfigured)
        {
            try
            {
                var create = new SetupIntentCreateOptions
                {
                    Usage = "off_session",
                    Metadata = new Dictionary<string, string>{{"user_id", userId.ToString()}},
                };
                var si = await _si.CreateAsync(create, cancellationToken: ct);
                setupIntentId = si.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create SetupIntent for {UserId}", userId);
            }
        }
        else
        {
            setupIntentId = $"seti_{userId.ToString("N").Substring(0,8)}_local";
        }

        if (state == null)
        {
            state = new PaymentSetupState { UserId = userId, HasPaymentMethod = false, LatestSetupIntentId = setupIntentId };
            _db.PaymentSetupStates.Add(state);
        }
        else
        {
            state.LatestSetupIntentId = setupIntentId ?? state.LatestSetupIntentId;
            state.UpdatedAtUtc = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<PaymentIntentLog> CreateFailurePaymentIntentAsync(Commitment commitment, CancellationToken ct = default)
    {
        // Attempt number based on existing logs for this commitment
        var existingSucceeded = await _db.PaymentIntentLogs
            .Where(p => p.CommitmentId == commitment.Id)
            .OrderByDescending(p => p.AttemptNumber)
            .FirstOrDefaultAsync(ct);
        var attempt = (existingSucceeded?.AttemptNumber ?? 0) + 1;
        var idempotencyKey = $"fail-{commitment.Id}-a{attempt}";

        // If a log already exists for this attempt, return it
        var existing = await _db.PaymentIntentLogs.FirstOrDefaultAsync(p => p.CommitmentId == commitment.Id && p.AttemptNumber == attempt, ct);
        if (existing != null) return existing;

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
                _logger.LogError(ex, "Stripe PaymentIntent creation failed for {CommitmentId} attempt {Attempt}", commitment.Id, attempt);
                paymentIntentId = idempotencyKey + "-sim";
                status = "failed";
            }
        }
        else
        {
            paymentIntentId = idempotencyKey + "-local";
        }

        var log = new PaymentIntentLog
        {
            CommitmentId = commitment.Id,
            StripePaymentIntentId = paymentIntentId,
            AmountMinor = commitment.StakeAmountMinor,
            Currency = commitment.Currency,
            Status = status,
            AttemptNumber = attempt,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.PaymentIntentLogs.Add(log);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException dup)
        {
            _logger.LogWarning(dup, "Duplicate payment intent log for commitment {CommitmentId} attempt {Attempt}", commitment.Id, attempt);
            // load existing and return (another thread won)
            var existingDup = await _db.PaymentIntentLogs.FirstAsync(p => p.CommitmentId == commitment.Id && p.AttemptNumber == attempt, ct);
            return existingDup;
        }
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
