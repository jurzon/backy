namespace Commitments.Domain.Entities;

public class ReminderEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CommitmentId { get; set; }
    public DateTime ScheduledForUtc { get; set; }
    public string Type { get; set; } = "reminder.checkin_due"; // event types per spec
    public string Status { get; set; } = "pending"; // pending|sent|skipped
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAtUtc { get; set; }
}
