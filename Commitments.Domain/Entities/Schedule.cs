namespace Commitments.Domain.Entities;

public class Schedule
{
    public Guid CommitmentId { get; set; }
    public SchedulePatternType PatternType { get; private set; }
    public int Interval { get; private set; } = 1; // every N units (days / weeks / months)
    public WeekdayMask Weekdays { get; private set; } = WeekdayMask.Monday; // for weekly
    public int? MonthDay { get; private set; } // for monthly by fixed day
    public int? NthWeek { get; private set; } // for monthly nth weekday (1..5 or -1 for last)
    public int? NthWeekday { get; private set; } // 0=Mon .. 6=Sun
    public DateOnly StartDate { get; private set; } // stored as local date in timezone
    public TimeOnly TimeOfDay { get; private set; } // local time
    public string Timezone { get; private set; } = "UTC"; // IANA/Windows accepted

    private Schedule() {}

    public static Schedule CreateDaily(DateOnly startDate, TimeOnly time, string timezone, int interval = 1)
        => new()
        {
            PatternType = SchedulePatternType.Daily,
            Interval = Math.Max(1, interval),
            StartDate = startDate,
            TimeOfDay = time,
            Timezone = timezone
        };

    public static Schedule CreateWeekly(DateOnly startDate, TimeOnly time, string timezone, WeekdayMask weekdays, int interval = 1)
        => new()
        {
            PatternType = SchedulePatternType.Weekly,
            Interval = Math.Max(1, interval),
            StartDate = startDate,
            TimeOfDay = time,
            Timezone = timezone,
            Weekdays = weekdays == WeekdayMask.None ? WeekdayMask.Monday : weekdays
        };

    public static Schedule CreateMonthlyByDay(DateOnly startDate, TimeOnly time, string timezone, int monthDay, int interval = 1)
        => new()
        {
            PatternType = SchedulePatternType.Monthly,
            Interval = Math.Max(1, interval),
            StartDate = startDate,
            TimeOfDay = time,
            Timezone = timezone,
            MonthDay = monthDay
        };

    public static Schedule CreateMonthlyByNthWeekday(DateOnly startDate, TimeOnly time, string timezone, int nthWeek, int nthWeekday, int interval = 1)
        => new()
        {
            PatternType = SchedulePatternType.Monthly,
            Interval = Math.Max(1, interval),
            StartDate = startDate,
            TimeOfDay = time,
            Timezone = timezone,
            NthWeek = nthWeek,
            NthWeekday = nthWeekday
        };

    private TimeZoneInfo ResolveTz()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(Timezone);
        }
        catch
        {
            // Fallbacks for common IANA on Windows or vice versa not handled exhaustively here.
            return TimeZoneInfo.Utc;
        }
    }

    private DateTime LocalToUtc(DateOnly date, TimeOnly time, TimeZoneInfo tz)
    {
        var unspecified = new DateTime(date.Year, date.Month, date.Day, time.Hour, time.Minute, time.Second, DateTimeKind.Unspecified);
        // Handle invalid times (DST gaps) by advancing minute until valid
        var probe = unspecified;
        for (int i = 0; i < 120; i++)
        {
            if (!tz.IsInvalidTime(probe))
            {
                if (tz.IsAmbiguousTime(probe))
                {
                    // choose first mapping (earliest UTC) by subtracting base offset difference
                    var offsets = tz.GetAmbiguousTimeOffsets(probe);
                    return new DateTimeOffset(probe, offsets[0]).UtcDateTime;
                }
                return TimeZoneInfo.ConvertTimeToUtc(probe, tz);
            }
            probe = probe.AddMinutes(1);
        }
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, tz); // fallback
    }

    private DateTime FirstOccurrenceUtc()
    {
        var tz = ResolveTz();
        return LocalToUtc(StartDate, TimeOfDay, tz);
    }

    public IEnumerable<DateTime> PreviewNextOccurrences(DateTime fromUtc, DateTime deadlineUtc, int count)
    {
        var list = new List<DateTime>();
        DateTime? next = NextOccurrence(fromUtc, deadlineUtc);
        while (next != null && next < deadlineUtc && list.Count < count)
        {
            list.Add(next.Value);
            next = NextOccurrence(next.Value, deadlineUtc);
        }
        return list;
    }

    public DateTime? NextOccurrence(DateTime afterUtc, DateTime deadlineUtc)
    {
        var first = FirstOccurrenceUtc();
        if (first >= deadlineUtc) return null;
        if (afterUtc < first) return first;

        return PatternType switch
        {
            SchedulePatternType.Daily => NextDaily(afterUtc, deadlineUtc),
            SchedulePatternType.Weekly => NextWeekly(afterUtc, deadlineUtc),
            SchedulePatternType.Monthly => NextMonthly(afterUtc, deadlineUtc),
            _ => null
        };
    }

    private DateTime? NextDaily(DateTime afterUtc, DateTime deadlineUtc)
    {
        var tz = ResolveTz();
        var afterLocal = TimeZoneInfo.ConvertTimeFromUtc(afterUtc, tz);
        var startLocal = new DateTime(StartDate.Year, StartDate.Month, StartDate.Day, TimeOfDay.Hour, TimeOfDay.Minute, TimeOfDay.Second);
        if (afterLocal < startLocal)
            return LocalToUtc(StartDate, TimeOfDay, tz);
        var daysSinceStart = (afterLocal.Date - startLocal.Date).Days;
        var nextIndex = daysSinceStart + 1;
        if (nextIndex % Interval != 0)
            nextIndex += (Interval - (nextIndex % Interval));
        var nextLocalDate = StartDate.AddDays(nextIndex);
        var nextUtc = LocalToUtc(nextLocalDate, TimeOfDay, tz);
        return nextUtc >= deadlineUtc ? null : nextUtc;
    }

    private DateTime? NextWeekly(DateTime afterUtc, DateTime deadlineUtc)
    {
        var tz = ResolveTz();
        var afterLocal = TimeZoneInfo.ConvertTimeFromUtc(afterUtc, tz);
        var startLocalDate = StartDate;
        // Determine week offset from start in local calendar
        var daysDiff = (afterLocal.Date - new DateTime(startLocalDate.Year, startLocalDate.Month, startLocalDate.Day)).Days;
        if (daysDiff < 0) return LocalToUtc(StartDate, TimeOfDay, tz);
        var weeksSinceStart = daysDiff / 7;
        var weekCursor = weeksSinceStart;
        for (int scanWeeks = 0; scanWeeks < 260; scanWeeks++) // ~5 years safety
        {
            if (weekCursor % Interval == 0)
            {
                var weekStartDate = startLocalDate.AddDays(weekCursor * 7);
                // iterate days in this week (Mon..Sun) respecting Weekdays mask
                for (int d = 0; d < 7; d++)
                {
                    var date = weekStartDate.AddDays(d);
                    var mask = DayOfWeekToMask((DayOfWeek)(((int)DayOfWeek.Monday + d) % 7));
                    if ((Weekdays & mask) == 0) continue;
                    var candidateUtc = LocalToUtc(date, TimeOfDay, tz);
                    if (candidateUtc > afterUtc)
                        return candidateUtc >= deadlineUtc ? null : candidateUtc;
                }
            }
            weekCursor++;
        }
        return null;
    }

    private DateTime? NextMonthly(DateTime afterUtc, DateTime deadlineUtc)
    {
        var tz = ResolveTz();
        var afterLocal = TimeZoneInfo.ConvertTimeFromUtc(afterUtc, tz);
        var monthCursor = new DateOnly(afterLocal.Year, afterLocal.Month, 1);
        var startMonth = new DateOnly(StartDate.Year, StartDate.Month, 1);
        for (int i = 0; i < 120; i++)
        {
            var monthsFromStart = ((monthCursor.Year - startMonth.Year) * 12) + (monthCursor.Month - startMonth.Month);
            if (monthsFromStart >= 0 && monthsFromStart % Interval == 0)
            {
                DateOnly? candidateDate = null;
                if (MonthDay != null)
                {
                    var day = Math.Min(MonthDay.Value, DateTime.DaysInMonth(monthCursor.Year, monthCursor.Month));
                    candidateDate = new DateOnly(monthCursor.Year, monthCursor.Month, day);
                }
                else if (NthWeek != null && NthWeekday != null)
                {
                    candidateDate = ComputeMonthlyNthDate(monthCursor.Year, monthCursor.Month, NthWeek.Value, NthWeekday.Value);
                }
                if (candidateDate != null)
                {
                    var candidateUtc = LocalToUtc(candidateDate.Value, TimeOfDay, tz);
                    if (candidateUtc > afterUtc)
                        return candidateUtc >= deadlineUtc ? null : candidateUtc;
                }
            }
            monthCursor = monthCursor.AddMonths(1);
        }
        return null;
    }

    private static DateOnly? ComputeMonthlyNthDate(int year, int month, int nthWeek, int nthWeekday)
    {
        if (nthWeekday < 0 || nthWeekday > 6) return null;
        if (nthWeek == -1)
        {
            // last weekday of month specified
            for (int day = DateTime.DaysInMonth(year, month); day >= 1; day--)
            {
                var dt = new DateTime(year, month, day);
                if (ConvertDowToCustom(dt.DayOfWeek) == nthWeekday) return new DateOnly(year, month, day);
            }
            return null;
        }
        if (nthWeek < 1 || nthWeek > 5) return null;
        var firstOfMonth = new DateTime(year, month, 1);
        // custom mapping Mon(0)..Sun(6); System Sunday=0
        int desiredDowSystem = (nthWeekday + 1) % 7; // convert custom Mon0 to Monday1 etc.
        int offset = desiredDowSystem - (int)firstOfMonth.DayOfWeek;
        if (offset < 0) offset += 7;
        var dayNum = 1 + offset + (nthWeek - 1) * 7;
        if (dayNum > DateTime.DaysInMonth(year, month)) return null;
        return new DateOnly(year, month, dayNum);
    }

    private static int ConvertDowToCustom(DayOfWeek dow) => dow switch
    {
        DayOfWeek.Monday => 0,
        DayOfWeek.Tuesday => 1,
        DayOfWeek.Wednesday => 2,
        DayOfWeek.Thursday => 3,
        DayOfWeek.Friday => 4,
        DayOfWeek.Saturday => 5,
        DayOfWeek.Sunday => 6,
        _ => 0
    };

    public int CountOccurrencesUpTo(DateTime untilExclusiveUtc, DateTime deadlineUtc, int safetyCap = 1000)
    {
        var count = 0;
        var first = FirstOccurrenceUtc();
        if (first >= untilExclusiveUtc || first >= deadlineUtc) return 0;
        var current = first;
        while (current < untilExclusiveUtc && current < deadlineUtc && count < safetyCap)
        {
            count++;
            var next = NextOccurrence(current, deadlineUtc);
            if (next == null) break;
            current = next.Value;
        }
        return count;
    }

    private static WeekdayMask DayOfWeekToMask(DayOfWeek dow) => dow switch
    {
        DayOfWeek.Monday => WeekdayMask.Monday,
        DayOfWeek.Tuesday => WeekdayMask.Tuesday,
        DayOfWeek.Wednesday => WeekdayMask.Wednesday,
        DayOfWeek.Thursday => WeekdayMask.Thursday,
        DayOfWeek.Friday => WeekdayMask.Friday,
        DayOfWeek.Saturday => WeekdayMask.Saturday,
        DayOfWeek.Sunday => WeekdayMask.Sunday,
        _ => WeekdayMask.None
    };
}
