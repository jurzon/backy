namespace Commitments.Domain.Entities;

public class PaymentIntentLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CommitmentId { get; set; }
    public string StripePaymentIntentId { get; set; } = string.Empty;
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = "EUR";
    public string Status { get; set; } = "created"; // created|requires_action|succeeded|failed|cancelled
    public string? LastErrorCode { get; set; }
    public int AttemptNumber { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
