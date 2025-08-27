namespace Commitments.Domain.Entities;

public class NotificationQuietHours
{
    public Guid UserId { get; set; }
    public int StartHour { get; set; } // 0-23 local hour in stored timezone
    public int EndHour { get; set; }   // 0-23 local hour
    public string Timezone { get; set; } = "UTC"; // IANA id
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
