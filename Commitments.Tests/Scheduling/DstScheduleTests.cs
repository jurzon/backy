using System;
using System.Linq;
using Commitments.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Commitments.Tests.Scheduling;

public class DstScheduleTests
{
    private static string GetEasternTzId()
    {
        var zones = TimeZoneInfo.GetSystemTimeZones();
        if (zones.Any(z => z.Id == "America/New_York")) return "America/New_York";
        if (zones.Any(z => z.Id == "Eastern Standard Time")) return "Eastern Standard Time"; // Windows
        return "UTC"; // fallback (test will early-exit)
    }

    private static DateOnly? FindSpringForwardDate(int year, TimeZoneInfo tz)
    {
        var d = new DateTime(year, 3, 1, 12, 0, 0, DateTimeKind.Unspecified);
        DateTime? prev = null;
        for (int i = 0; i < 46; i++)
        {
            var cur = d.AddDays(i);
            if (prev != null)
            {
                var prevDst = tz.IsDaylightSavingTime(prev.Value);
                var curDst = tz.IsDaylightSavingTime(cur);
                if (!prevDst && curDst) return DateOnly.FromDateTime(cur);
            }
            prev = cur;
        }
        return null;
    }

    private static DateOnly? FindFallBackDate(int year, TimeZoneInfo tz)
    {
        var d = new DateTime(year, 10, 1, 12, 0, 0, DateTimeKind.Unspecified);
        DateTime? prev = null;
        for (int i = 0; i < 62; i++)
        {
            var cur = d.AddDays(i);
            if (prev != null)
            {
                var prevDst = tz.IsDaylightSavingTime(prev.Value);
                var curDst = tz.IsDaylightSavingTime(cur);
                if (prevDst && !curDst) return DateOnly.FromDateTime(cur);
            }
            prev = cur;
        }
        return null;
    }

    [Fact]
    public void DailyOccurrencesCrossSpringForwardProduces23hGap()
    {
        var tzId = GetEasternTzId();
        if (tzId == "UTC") return; // environment lacks DST zone
        var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
        var year = DateTime.UtcNow.Year + 1; // future year ensures transition not in past
        var spring = FindSpringForwardDate(year, tz);
        spring.Should().NotBeNull();
        var startDate = spring!.Value.AddDays(-2); // start 2 days before
        var schedule = Schedule.CreateDaily(startDate, new TimeOnly(2, 0), tzId, 1);
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(new DateTime(startDate.Year, startDate.Month, startDate.Day, 0, 0, 0, DateTimeKind.Unspecified), tz).AddHours(-1);
        var deadline = fromUtc.AddDays(10);
        var occ = schedule.PreviewNextOccurrences(fromUtc, deadline, 7).OrderBy(o => o).ToList();
        occ.Count.Should().BeGreaterThan(3);
        var gaps = occ.Skip(1).Zip(occ, (a, b) => a - b).ToList();
        gaps.Should().Contain(g => g.TotalHours > 22.5 && g.TotalHours < 23.5, "should have a 23h gap across spring forward");
    }

    [Fact]
    public void DailyOccurrencesCrossFallBackProduces25hGap()
    {
        var tzId = GetEasternTzId();
        if (tzId == "UTC") return; // environment lacks DST zone
        var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
        var year = DateTime.UtcNow.Year + 1;
        var fall = FindFallBackDate(year, tz);
        fall.Should().NotBeNull();
        var startDate = fall!.Value.AddDays(-2);
        var schedule = Schedule.CreateDaily(startDate, new TimeOnly(2, 0), tzId, 1);
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(new DateTime(startDate.Year, startDate.Month, startDate.Day, 0, 0, 0, DateTimeKind.Unspecified), tz).AddHours(-1);
        var deadline = fromUtc.AddDays(15);
        var occ = schedule.PreviewNextOccurrences(fromUtc, deadline, 10).OrderBy(o => o).ToList();
        occ.Count.Should().BeGreaterThan(3);
        var gaps = occ.Skip(1).Zip(occ, (a, b) => a - b).ToList();
        gaps.Should().Contain(g => g.TotalHours > 24.5 && g.TotalHours < 25.5, "should have a 25h gap across fall back");
    }
}
