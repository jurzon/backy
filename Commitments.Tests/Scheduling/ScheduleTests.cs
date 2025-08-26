using Commitments.Domain.Entities;
using FluentAssertions;

namespace Commitments.Tests.Scheduling;

public class ScheduleTests
{
    [Fact]
    public void Daily_next_occurrence_before_deadline()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var schedule = Schedule.CreateDaily(today, new TimeOnly(9,0), "UTC", 1);
        var deadline = DateTime.UtcNow.AddDays(5);
        var list = schedule.PreviewNextOccurrences(deadline, 3).ToList();
        list.Should().OnlyContain(d => d < deadline);
    }

    [Fact]
    public void Weekly_respects_selected_days()
    {
        var start = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var schedule = Schedule.CreateWeekly(start, new TimeOnly(8,0), "UTC", WeekdayMask.Monday | WeekdayMask.Friday, 1);
        var deadline = DateTime.UtcNow.AddDays(14);
        var next = schedule.PreviewNextOccurrences(deadline, 5).ToList();
        next.Should().OnlyContain(d => d < deadline);
    }

    [Fact]
    public void Monthly_day_clamp_does_not_exceed_deadline()
    {
        var now = DateTime.UtcNow;
        var targetDay = 31;
        var start = new DateOnly(now.Year, now.Month, Math.Min(DateTime.DaysInMonth(now.Year, now.Month), targetDay));
        var schedule = Schedule.CreateMonthlyByDay(start, new TimeOnly(10,0), "UTC", targetDay, 1);
        var deadline = now.AddMonths(3);
        var occurrences = schedule.PreviewNextOccurrences(deadline, 3).ToList();
        occurrences.Should().OnlyContain(o => o < deadline);
    }
}
