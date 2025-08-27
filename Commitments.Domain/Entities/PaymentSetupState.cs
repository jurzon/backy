namespace Commitments.Domain.Entities;

public class PaymentSetupState
{
    public Guid UserId { get; set; }
    public bool HasPaymentMethod { get; set; }
    public string? LatestSetupIntentId { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
