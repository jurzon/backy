using Commitments.Api.Notifications;
using Commitments.Domain.Abstractions;
using Commitments.Domain.Entities;
using Commitments.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Commitments.Tests.Notifications;

public class ReminderNotificationDispatcherTests
{
    private sealed class FakeClock : IClock { public DateTime UtcNow { get; set; } }

    private static AppDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new AppDbContext(opts);
    }

    private static Commitment SeedCommitment(AppDbContext db, DateTime deadline)
    {
        var schedule = Schedule.CreateDaily(DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-2), new TimeOnly(9,0), "UTC", 1);
        var c = Commitment.Create(Guid.NewGuid(), "Goal", 100, "EUR", deadline, "UTC", schedule);
        db.Commitments.Add(c);
        db.SaveChanges();
        return c;
    }

    [Fact]
    public async Task SendsPendingRemindersOutsideQuietHours()
    {
        using var db = CreateDb();
        var clock = new FakeClock { UtcNow = DateTime.UtcNow.Date.AddHours(12) }; // midday
        var commitment = SeedCommitment(db, clock.UtcNow.AddDays(5));
        db.ReminderEvents.Add(new ReminderEvent { CommitmentId = commitment.Id, ScheduledForUtc = clock.UtcNow.AddMinutes(-5), Type = "reminder.checkin_due" });
        db.SaveChanges();
        var sender = new InMemoryNotificationSender();
        var dispatcher = new ReminderNotificationDispatcher(db, sender, clock);
        await dispatcher.DispatchAsync();
        sender.Sent.Should().HaveCount(1);
        db.ReminderEvents.Single().Status.Should().Be("sent");
    }

    [Fact]
    public async Task DefersInQuietHours()
    {
        using var db = CreateDb();
        var clock = new FakeClock { UtcNow = DateTime.UtcNow.Date.AddHours(23) }; // 23:00
        var commitment = SeedCommitment(db, clock.UtcNow.AddDays(5));
        db.ReminderEvents.Add(new ReminderEvent { CommitmentId = commitment.Id, ScheduledForUtc = clock.UtcNow.AddMinutes(-10), Type = "reminder.checkin_due" });
        db.SaveChanges();
        var sender = new InMemoryNotificationSender();
        var dispatcher = new ReminderNotificationDispatcher(db, sender, clock);
        await dispatcher.DispatchAsync();
        sender.Sent.Should().BeEmpty();
        var rem = db.ReminderEvents.Single();
        rem.Status.Should().Be("pending");
        rem.ScheduledForUtc.Should().BeAfter(clock.UtcNow); // rescheduled
        rem.Type.Should().Contain("d=1");
    }

    [Fact]
    public async Task SendsAfterQuietEnd()
    {
        using var db = CreateDb();
        var start = DateTime.UtcNow.Date.AddHours(23); // inside quiet 23:00
        var clock = new FakeClock { UtcNow = start };
        var commitment = SeedCommitment(db, clock.UtcNow.AddDays(5));
        db.ReminderEvents.Add(new ReminderEvent { CommitmentId = commitment.Id, ScheduledForUtc = start.AddMinutes(-1), Type = "reminder.checkin_due" });
        db.SaveChanges();
        var sender = new InMemoryNotificationSender();
        var dispatcher = new ReminderNotificationDispatcher(db, sender, clock);
        // First dispatch defers
        await dispatcher.DispatchAsync();
        var rem = db.ReminderEvents.Single();
        rem.Status.Should().Be("pending");
        rem.Type.Should().Contain("d=1");
        rem.ScheduledForUtc.Should().BeAfter(clock.UtcNow);
        // Advance clock to deferred time (quiet end boundary)
        clock.UtcNow = rem.ScheduledForUtc;
        // Second dispatch sends
        await dispatcher.DispatchAsync();
        rem.Status.Should().Be("sent");
        sender.Sent.Should().HaveCount(1);
    }
}
