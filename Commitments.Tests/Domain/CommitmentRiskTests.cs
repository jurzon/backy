using System;
using Commitments.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Commitments.Tests.Domain;

public class CommitmentRiskTests
{
    private static Commitment NewBaselineCommitment(DateTime now)
    {
        // Long enough deadline to ensure many occurrences exist (30 days ahead)
        var deadline = now.AddDays(30);
        var start = DateOnly.FromDateTime(now.Date).AddDays(-30);
        var schedule = Schedule.CreateDaily(start, new TimeOnly(9, 0), "UTC", 1);
        return Commitment.Create(Guid.NewGuid(), "Goal", 100, "EUR", deadline, "UTC", schedule);
    }

    private static void AddCheckIns(Commitment c, int count)
    {
        for (int i = 0; i < count; i++) c.AddCheckIn(null, null);
    }

    private static int Expected(Commitment c, DateTime asOf) => c.Schedule!.CountOccurrencesUpTo(asOf, c.DeadlineUtc);

    [Fact]
    public void CriticalWhenLessThan24hAndLowAdherence()
    {
        var now = DateTime.UtcNow;
        var c = NewBaselineCommitment(now);
        var simulatedNow = c.DeadlineUtc.AddHours(-12); // inside <24h window
        var expected = Expected(c, simulatedNow);
        AddCheckIns(c, (int)Math.Floor(expected * 0.4)); // <0.5 adherence
        c.ComputeRiskBadge(simulatedNow).Should().Be("Critical");
    }

    [Fact]
    public void AtRiskWhenLessThan24hAndModerateAdherence()
    {
        var now = DateTime.UtcNow;
        var c = NewBaselineCommitment(now);
        var simulatedNow = c.DeadlineUtc.AddHours(-20);
        var expected = Expected(c, simulatedNow);
        AddCheckIns(c, (int)Math.Round(expected * 0.6)); // between 0.5 and 0.75
        c.ComputeRiskBadge(simulatedNow).Should().Be("AtRisk");
    }

    [Fact]
    public void OnTrackWhenHighAdherence()
    {
        var now = DateTime.UtcNow;
        var c = NewBaselineCommitment(now);
        var simulatedNow = now.AddDays(10);
        var expected = Expected(c, simulatedNow);
        AddCheckIns(c, (int)Math.Ceiling(expected * 0.95));
        c.ComputeRiskBadge(simulatedNow).Should().Be("OnTrack");
    }

    [Fact]
    public void SlightlyBehindWhenMediumHighAdherence()
    {
        var now = DateTime.UtcNow;
        var c = NewBaselineCommitment(now);
        var simulatedNow = now.AddDays(7);
        var expected = Expected(c, simulatedNow);
        AddCheckIns(c, (int)Math.Round(expected * 0.72));
        c.ComputeRiskBadge(simulatedNow).Should().Be("SlightlyBehind");
    }

    [Fact]
    public void BehindWhenHalfAdherence()
    {
        var now = DateTime.UtcNow;
        var c = NewBaselineCommitment(now);
        var simulatedNow = now.AddDays(5);
        var expected = Expected(c, simulatedNow);
        AddCheckIns(c, (int)Math.Round(expected * 0.5));
        c.ComputeRiskBadge(simulatedNow).Should().Be("Behind");
    }

    [Fact]
    public void AtRiskGeneralWhenLowAdherenceAndMoreThanDayLeft()
    {
        var now = DateTime.UtcNow;
        var c = NewBaselineCommitment(now);
        var simulatedNow = now.AddDays(6);
        var expected = Expected(c, simulatedNow);
        AddCheckIns(c, (int)Math.Round(expected * 0.4));
        c.ComputeRiskBadge(simulatedNow).Should().Be("AtRisk");
    }

    [Fact]
    public void DecisionNeededOverridesOtherLogic()
    {
        var now = DateTime.UtcNow;
        var c = NewBaselineCommitment(now);
        // Simulate reaching deadline to transition
        c.TransitionToDecisionNeeded(TimeSpan.FromHours(1));
        var simulatedNow = c.DeadlineUtc.AddMinutes(-30);
        c.ComputeRiskBadge(simulatedNow).Should().Be("DecisionNeeded");
    }
}
