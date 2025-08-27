using Commitments.Api.Background;
using Commitments.Domain.Entities;
using Commitments.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Commitments.Domain.Abstractions;

namespace Commitments.Tests.Background;

public class GraceExpiryScannerTests
{
    private sealed class FakeClock : IClock { public DateTime UtcNow { get; set; } }

    private AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private Commitment NewCommitment(DateTime now, DateTime deadlineUtc)
    {
        var startDate = DateOnly.FromDateTime(now.Date).AddDays(-2);
        var schedule = Schedule.CreateDaily(startDate, new TimeOnly(9,0), "UTC", 1);
        return Commitment.Create(Guid.NewGuid(), "Goal", 100, "EUR", deadlineUtc, "UTC", schedule);
    }

    private static DateTime DeadlineAfterNextOccurrence(DateTime now, int hoursAfterNext = 2)
    {
        var nextDaily = now.Date.AddDays(1).AddHours(9); // schedule at 09:00 next day
        return nextDaily.AddHours(hoursAfterNext); // ensure deadline after occurrence
    }

    [Fact]
    public async Task Transitions_active_past_deadline_to_decision_needed_and_sets_grace()
    {
        using var db = CreateDb();
        var clock = new FakeClock { UtcNow = DateTime.UtcNow };
        var deadline = DeadlineAfterNextOccurrence(clock.UtcNow, 3); // > next occurrence
        var c = NewCommitment(clock.UtcNow, deadline);
        db.Commitments.Add(c);
        await db.SaveChangesAsync();
        clock.UtcNow = deadline.AddMinutes(5);
        var scanner = new GraceExpiryScanner(db, clock);
        await scanner.ScanAsync();
        c.Status.Should().Be(CommitmentStatus.DecisionNeeded);
        c.GraceExpiresUtc.Should().Be(deadline.AddMinutes(60));
    }

    [Fact]
    public async Task Creates_final_warning_event_if_inside_final_window()
    {
        using var db = CreateDb();
        var clock = new FakeClock { UtcNow = DateTime.UtcNow };
        var deadline = DeadlineAfterNextOccurrence(clock.UtcNow, 4);
        var c = NewCommitment(clock.UtcNow, deadline);
        db.Commitments.Add(c);
        await db.SaveChangesAsync();
        clock.UtcNow = deadline.AddMinutes(1);
        var scanner = new GraceExpiryScanner(db, clock);
        await scanner.ScanAsync();
        var fw = db.ReminderEvents.FirstOrDefault(r => r.CommitmentId == c.Id && r.Type == "commitment.grace_final_warning");
        fw.Should().NotBeNull();
        fw!.ScheduledForUtc.Should().Be(c.GraceExpiresUtc!.Value.AddMinutes(-15));
    }

    [Fact]
    public async Task Auto_fails_expired_grace()
    {
        using var db = CreateDb();
        var clock = new FakeClock { UtcNow = DateTime.UtcNow };
        var deadline = DeadlineAfterNextOccurrence(clock.UtcNow, 2);
        var c = NewCommitment(clock.UtcNow, deadline);
        db.Commitments.Add(c);
        await db.SaveChangesAsync();
        var scanner = new GraceExpiryScanner(db, clock);
        clock.UtcNow = deadline.AddMinutes(2);
        await scanner.ScanAsync();
        c.Status.Should().Be(CommitmentStatus.DecisionNeeded);
        clock.UtcNow = deadline.AddMinutes(62);
        await scanner.ScanAsync();
        c.Status.Should().Be(CommitmentStatus.Failed);
    }
}
