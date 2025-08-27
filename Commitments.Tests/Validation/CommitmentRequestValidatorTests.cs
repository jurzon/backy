using Commitments.Api.Endpoints;
using Commitments.Api.Validation;
using FluentAssertions;

namespace Commitments.Tests.Validation;

public class CommitmentRequestValidatorTests
{
    private static CreateCommitmentRequest MakeValid(Action<CreateCommitmentRequest>? mutate = null)
    {
        var req = new CreateCommitmentRequest(
            UserId: Guid.NewGuid(),
            Goal: "Read 10 pages",
            StakeAmount: 10.5m,
            Currency: "EUR",
            DeadlineUtc: DateTime.UtcNow.AddHours(2),
            Timezone: "Europe/Bratislava",
            Schedule: new ScheduleDto(
                PatternType: "daily",
                Interval: 1,
                WeekdaysMask: null,
                MonthDay: null,
                NthWeek: null,
                NthWeekday: null,
                StartDate: DateOnly.FromDateTime(DateTime.UtcNow.Date),
                TimeOfDay: new TimeOnly(9, 0))
        );
        mutate?.Invoke(req);
        return req;
    }

    [Fact]
    public void ValidRequestHasNoErrors()
    {
        var req = MakeValid();
        var errors = CommitmentRequestValidator.Validate(req);
        errors.Should().BeEmpty();
    }

    [Fact]
    public void StakeMustBePositive()
    {
        var req = MakeValid(r => r = r with { StakeAmount = 0 });
        var errors = CommitmentRequestValidator.Validate(req with { StakeAmount = 0 });
        errors.Keys.Should().Contain("stakeAmount");
    }

    [Fact]
    public void DeadlineMustBeFuturePlusOneHour()
    {
        var req = MakeValid();
        var near = DateTime.UtcNow.AddMinutes(30);
        var errors = CommitmentRequestValidator.Validate(req with { DeadlineUtc = near });
        errors.Keys.Should().Contain("deadlineUtc");
    }

    [Fact]
    public void WeeklyRequiresWeekdaysMask()
    {
        var req = MakeValid(r => r = r with { Schedule = r.Schedule with { PatternType = "weekly" } });
        var adjusted = req with { Schedule = req.Schedule with { PatternType = "weekly", WeekdaysMask = null } };
        var errors = CommitmentRequestValidator.Validate(adjusted);
        errors.Keys.Should().Contain(k => k.StartsWith("schedule.weekdaysMask"));
    }

    [Fact]
    public void MonthlyDayRequiresMonthDay()
    {
        var req = MakeValid();
        var adjusted = req with { Schedule = req.Schedule with { PatternType = "monthly_day", MonthDay = null } };
        var errors = CommitmentRequestValidator.Validate(adjusted);
        errors.Keys.Should().Contain(k => k.StartsWith("schedule.monthDay"));
    }

    [Fact]
    public void MonthlyNthRequiresNthWeekAndNthWeekday()
    {
        var req = MakeValid();
        var adjusted = req with { Schedule = req.Schedule with { PatternType = "monthly_nth", NthWeek = null, NthWeekday = null } };
        var errors = CommitmentRequestValidator.Validate(adjusted);
        errors.Keys.Should().Contain(k => k.StartsWith("schedule.nthWeek"));
        errors.Keys.Should().Contain(k => k.StartsWith("schedule.nthWeekday"));
    }
}
