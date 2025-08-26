using System.ComponentModel.DataAnnotations;

namespace Commitments.Domain.Entities;

public class Commitment
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid UserId { get; private set; }
    [MaxLength(200)] public string Goal { get; private set; } = string.Empty;
    public long StakeAmountMinor { get; private set; }
    [MaxLength(3)] public string Currency { get; private set; } = "EUR"; // ISO 4217
    public DateTime DeadlineUtc { get; private set; }
    public string Timezone { get; private set; } = "UTC"; // IANA
    public CommitmentStatus Status { get; private set; } = CommitmentStatus.Active;
    public DateTime? GraceExpiresUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime? CancelledAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? FailedAtUtc { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }
    public DateTime EditingLockedAtUtc { get; private set; }

    public Schedule? Schedule { get; private set; }
    public List<CheckIn> CheckIns { get; private set; } = new();

    private Commitment() {}

    public static Commitment Create(Guid userId, string goal, long stakeAmountMinor, string currency, DateTime deadlineUtc, string timezone, Schedule schedule)
    {
        if (string.IsNullOrWhiteSpace(goal)) throw new ArgumentException("Goal required", nameof(goal));
        if (goal.Length > 200) throw new ArgumentException("Goal too long", nameof(goal));
        if (stakeAmountMinor <= 0) throw new ArgumentException("Stake must be > 0", nameof(stakeAmountMinor));
        if (deadlineUtc <= DateTime.UtcNow.AddHours(1)) throw new ArgumentException("Deadline min 1h ahead", nameof(deadlineUtc));

        var c = new Commitment
        {
            UserId = userId,
            Goal = goal.Trim(),
            StakeAmountMinor = stakeAmountMinor,
            Currency = currency.ToUpperInvariant(),
            DeadlineUtc = DateTime.SpecifyKind(deadlineUtc, DateTimeKind.Utc),
            Timezone = timezone,
            EditingLockedAtUtc = deadlineUtc.AddHours(-24)
        };
        c.AttachSchedule(schedule);
        c.ValidateScheduleHasOccurrenceBeforeDeadline();
        return c;
    }

    public void AttachSchedule(Schedule schedule)
    {
        Schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
    }

    private void ValidateScheduleHasOccurrenceBeforeDeadline()
    {
        if (Schedule == null) throw new InvalidOperationException("Schedule missing");
        var next = Schedule.PreviewNextOccurrences(DeadlineUtc, 1).FirstOrDefault();
        if (next == default || next >= DeadlineUtc)
            throw new InvalidOperationException("Schedule must occur before deadline");
    }

    public bool IsInGrace => Status == CommitmentStatus.DecisionNeeded && GraceExpiresUtc != null && DateTime.UtcNow <= GraceExpiresUtc;

    public void TransitionToDecisionNeeded(TimeSpan graceWindow)
    {
        if (Status != CommitmentStatus.Active) throw new InvalidOperationException("Must be active");
        Status = CommitmentStatus.DecisionNeeded;
        GraceExpiresUtc = DeadlineUtc + graceWindow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Complete()
    {
        if (Status != CommitmentStatus.DecisionNeeded) throw new InvalidOperationException("Not in decision state");
        Status = CommitmentStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Fail()
    {
        if (Status != CommitmentStatus.DecisionNeeded && Status != CommitmentStatus.Active)
            throw new InvalidOperationException("Can only fail from active/decision");
        Status = CommitmentStatus.Failed;
        FailedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Cancel()
    {
        if (Status != CommitmentStatus.Active) throw new InvalidOperationException("Only active can cancel");
        if (DateTime.UtcNow >= EditingLockedAtUtc) throw new InvalidOperationException("Locked window");
        Status = CommitmentStatus.Cancelled;
        CancelledAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void SoftDelete()
    {
        if (Status is CommitmentStatus.Active or CommitmentStatus.DecisionNeeded)
            throw new InvalidOperationException("Cannot delete active/grace");
        Status = CommitmentStatus.Deleted;
        DeletedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public CheckIn AddCheckIn(string? note, string? photoUrl)
    {
        if (Status != CommitmentStatus.Active) throw new InvalidOperationException("Check-in only when active");
        var ci = new CheckIn
        {
            Id = Guid.NewGuid(),
            CommitmentId = Id,
            OccurredAtUtc = DateTime.UtcNow,
            Note = note,
            PhotoUrl = photoUrl,
            CreatedAtUtc = DateTime.UtcNow
        };
        CheckIns.Add(ci);
        UpdatedAtUtc = DateTime.UtcNow;
        return ci;
    }
}
