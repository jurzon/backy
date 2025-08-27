using Commitments.Api.Notifications;
using Commitments.Domain.Abstractions;
using Commitments.Domain.Entities;
using Commitments.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Commitments.Tests.Notifications;

public class QuietHoursTests
{
    [Fact]
    public async Task CustomOvernightQuietHours_DefersInsideWindow()
    {
        using var db = CreateDb();
        var clock = new TestClock { UtcNow = DateTime.UtcNow.Date.AddHours(23) }; // 23:00
        var commitment = SeedCommitment(db, clock.UtcNow.AddDays(2));
        db.NotificationQuietHours.Add(new NotificationQuietHours { UserId = commitment.UserId, StartHour = 22, EndHour = 7, Timezone = "UTC" });
        db.ReminderEvents.Add(new ReminderEvent { CommitmentId = commitment.Id, ScheduledForUtc = clock.UtcNow.AddMinutes(-5), Type = "reminder.checkin_due" });
        db.SaveChanges();
        var sender = new InMemoryNotificationSender();
        var dispatcher = new ReminderNotificationDispatcher(db, sender, clock);

        await dispatcher.DispatchAsync();

        sender.Sent.Should().BeEmpty();
        var rem = db.ReminderEvents.Single();
        rem.Status.Should().Be("pending");
        rem.Type.Should().Contain("d=1");
        rem.ScheduledForUtc.Should().BeAfter(clock.UtcNow);
    }

    [Fact]
    public async Task CustomDaytimeQuietHours_DefersInsideWindow()
    {
        using var db = CreateDb();
        var baseDay = DateTime.UtcNow.Date;
        var clock = new TestClock { UtcNow = baseDay.AddHours(10) }; // 10:00
        var commitment = SeedCommitment(db, clock.UtcNow.AddDays(1));
        db.NotificationQuietHours.Add(new NotificationQuietHours { UserId = commitment.UserId, StartHour = 9, EndHour = 17, Timezone = "UTC" });
        db.ReminderEvents.Add(new ReminderEvent { CommitmentId = commitment.Id, ScheduledForUtc = clock.UtcNow.AddMinutes(-1), Type = "reminder.checkin_due" });
        db.SaveChanges();
        var sender = new InMemoryNotificationSender();
        var dispatcher = new ReminderNotificationDispatcher(db, sender, clock);

        await dispatcher.DispatchAsync();

        sender.Sent.Should().BeEmpty();
        var rem = db.ReminderEvents.Single();
        rem.Status.Should().Be("pending");
        rem.Type.Should().Contain("d=1");
    }

    [Fact]
    public async Task SendsOutsideCustomDaytimeQuietHours()
    {
        using var db = CreateDb();
        var baseDay = DateTime.UtcNow.Date;
        var clock = new TestClock { UtcNow = baseDay.AddHours(18) }; // 18:00 outside 9-17
        var commitment = SeedCommitment(db, clock.UtcNow.AddDays(1));
        db.NotificationQuietHours.Add(new NotificationQuietHours { UserId = commitment.UserId, StartHour = 9, EndHour = 17, Timezone = "UTC" });
        db.ReminderEvents.Add(new ReminderEvent { CommitmentId = commitment.Id, ScheduledForUtc = clock.UtcNow.AddMinutes(-2), Type = "reminder.checkin_due" });
        db.SaveChanges();
        var sender = new InMemoryNotificationSender();
        var dispatcher = new ReminderNotificationDispatcher(db, sender, clock);

        await dispatcher.DispatchAsync();

        sender.Sent.Should().HaveCount(1);
        db.ReminderEvents.Single().Status.Should().Be("sent");
    }

    [Fact]
    public async Task SendsWhenDeferralLimitReachedInsideQuiet()
    {
        using var db = CreateDb();
        var clock = new TestClock { UtcNow = DateTime.UtcNow.Date.AddHours(23) }; // 23:00 inside quiet 22-7
        var commitment = SeedCommitment(db, clock.UtcNow.AddDays(1));
        db.NotificationQuietHours.Add(new NotificationQuietHours { UserId = commitment.UserId, StartHour = 22, EndHour = 7, Timezone = "UTC" });
        // Already deferred 3 times (MaxDeferrals=3) so dispatcher should send now
        db.ReminderEvents.Add(new ReminderEvent { CommitmentId = commitment.Id, ScheduledForUtc = clock.UtcNow.AddMinutes(-3), Type = "reminder.checkin_due|d=3" });
        db.SaveChanges();
        var sender = new InMemoryNotificationSender();
        var dispatcher = new ReminderNotificationDispatcher(db, sender, clock);

        await dispatcher.DispatchAsync();

        sender.Sent.Should().HaveCount(1);
        var rem = db.ReminderEvents.Single();
        rem.Status.Should().Be("sent");
    }

    private sealed class TestClock : IClock { public DateTime UtcNow { get; set; } }

    private static AppDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new AppDbContext(opts);
    }

    private static Commitment SeedCommitment(AppDbContext db, DateTime deadline)
    {
        var schedule = Schedule.CreateDaily(DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-1), new TimeOnly(9,0), "UTC", 1);
        var c = Commitment.Create(Guid.NewGuid(), "Goal", 100, "EUR", deadline, "UTC", schedule);
        db.Commitments.Add(c);
        db.SaveChanges();
        return c;
    }
}
