using System;
using Commitments.Domain.Entities;
using FluentAssertions;

namespace Commitments.Tests.Domain;

public class CommitmentTransitionTests
{
    private Commitment NewActive(DateTime deadlineUtc)
    {
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-2);
        var schedule = Schedule.CreateDaily(startDate, new TimeOnly(8,0), "UTC", 1);
        return Commitment.Create(Guid.NewGuid(), "Goal", 100, "EUR", deadlineUtc, "UTC", schedule);
    }

    [Fact]
    public void Cannot_cancel_when_not_active()
    {
        var c = NewActive(DateTime.UtcNow.AddDays(30));
        c.Fail();
        var act = () => c.Cancel();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Cannot_complete_unless_decision_needed()
    {
        var c = NewActive(DateTime.UtcNow.AddDays(30));
        var act = () => c.Complete();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Complete_works_in_decision_needed()
    {
        var c = NewActive(DateTime.UtcNow.AddDays(30));
        c.TransitionToDecisionNeeded(TimeSpan.FromMinutes(30));
        c.Complete();
        c.Status.Should().Be(CommitmentStatus.Completed);
    }

    [Fact]
    public void Fail_allowed_from_active()
    {
        var c = NewActive(DateTime.UtcNow.AddDays(30));
        c.Fail();
        c.Status.Should().Be(CommitmentStatus.Failed);
    }

    [Fact]
    public void Fail_allowed_from_decision_needed()
    {
        var c = NewActive(DateTime.UtcNow.AddDays(30));
        c.TransitionToDecisionNeeded(TimeSpan.FromMinutes(10));
        c.Fail();
        c.Status.Should().Be(CommitmentStatus.Failed);
    }

    [Fact]
    public void Cannot_delete_active()
    {
        var c = NewActive(DateTime.UtcNow.AddDays(30));
        var act = () => c.SoftDelete();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Cannot_cancel_inside_locked_window()
    {
        // locked window begins 24h before deadline. Use deadline 23h ahead => cancel should throw locked.
        var c = NewActive(DateTime.UtcNow.AddHours(23));
        var act = () => c.Cancel();
        act.Should().Throw<InvalidOperationException>().WithMessage("*Locked*");
    }
}
