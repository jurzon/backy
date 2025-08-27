using Commitments.Api.Background;
using Commitments.Domain.Entities;
using Commitments.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Commitments.Domain.Abstractions;

namespace Commitments.Tests.Background;

public class ReminderSchedulerTests
{
    // Public test methods first (SA1202)
    [Fact]
    public async Task BuildsEventsForActiveCommitment()
    {
        using var db = CreateDb();
        var clock = new FakeClock { UtcNow = DateTime.UtcNow };
        var schedule = Schedule.CreateDaily(DateOnly.FromDateTime(DateTime.UtcNow.Date), new TimeOnly(9, 0), "UTC", 1);
        var commitment = SeedCommitment(db, schedule);
        var scheduler = new ReminderScheduler(db, clock);

        await scheduler.BuildHorizonAsync();

        db.ReminderEvents.Count(re => re.CommitmentId == commitment.Id).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task IdempotentDoesNotDuplicate()
    {
        using var db = CreateDb();
        var clock = new FakeClock { UtcNow = DateTime.UtcNow };
        var schedule = Schedule.CreateDaily(DateOnly.FromDateTime(DateTime.UtcNow.Date), new TimeOnly(9, 0), "UTC", 1);
        var commitment = SeedCommitment(db, schedule);
        var scheduler = new ReminderScheduler(db, clock);

        await scheduler.BuildHorizonAsync();
        var firstCount = db.ReminderEvents.Count(re => re.CommitmentId == commitment.Id);
        await scheduler.BuildHorizonAsync();
        var secondCount = db.ReminderEvents.Count(re => re.CommitmentId == commitment.Id);

        secondCount.Should().Be(firstCount);
    }

    [Fact]
    public async Task SkipsNonActiveCommitments()
    {
        using var db = CreateDb();
        var clock = new FakeClock { UtcNow = DateTime.UtcNow };
        var schedule = Schedule.CreateDaily(DateOnly.FromDateTime(DateTime.UtcNow.Date), new TimeOnly(9, 0), "UTC", 1);
        var commitment = SeedCommitment(db, schedule);
        commitment.Fail();
        db.SaveChanges();
        var scheduler = new ReminderScheduler(db, clock);

        await scheduler.BuildHorizonAsync();

        db.ReminderEvents.Count(re => re.CommitmentId == commitment.Id).Should().Be(0);
    }

    // Helpers after tests
    private sealed class FakeClock : IClock
    {
        public DateTime UtcNow { get; set; }
    }

    private static AppDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(opts);
    }

    private static Commitment SeedCommitment(AppDbContext db, Schedule schedule, DateTime? deadlineUtc = null)
    {
        var deadline = deadlineUtc ?? DateTime.UtcNow.AddDays(10);
        var c = Commitment.Create(Guid.NewGuid(), "Test goal", 500, "EUR", deadline, "UTC", schedule);
        db.Commitments.Add(c);
        db.SaveChanges();
        return c;
    }
}
