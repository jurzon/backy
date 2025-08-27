namespace Commitments.Domain.Entities;

public class Schedule
{
    public Guid CommitmentId { get; set; }
    public SchedulePatternType PatternType { get; private set; }
    public int Interval { get; private set; } = 1; // every N units (days / weeks / months)
    public WeekdayMask Weekdays { get; private set; } = WeekdayMask.Monday; // for weekly
    public int? MonthDay { get; private set; } // for monthly by fixed day
    public int? NthWeek { get; private set; } // for monthly nth weekday (1..5 or -1 for last TBD later)
    public int? NthWeekday { get; private set; } // 0=Mon .. 6=Sun
    public DateOnly StartDate { get; private set; }
    public TimeOnly TimeOfDay { get; private set; }
    public string Timezone { get; private set; } = "UTC"; // (future use)

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
            SchedulePatternType.Daily => NextDaily(afterUtc, deadlineUtc, first),
            SchedulePatternType.Weekly => NextWeekly(afterUtc, deadlineUtc, first),
            SchedulePatternType.Monthly => NextMonthly(afterUtc, deadlineUtc, first),
            _ => null
        };
    }

    private DateTime FirstOccurrenceUtc() => new(StartDate.Year, StartDate.Month, StartDate.Day, TimeOfDay.Hour, TimeOfDay.Minute, TimeOfDay.Second, DateTimeKind.Utc);

    private DateTime? NextDaily(DateTime afterUtc, DateTime deadlineUtc, DateTime first)
    {
        var totalDays = (afterUtc - first).TotalDays;
        var daysSinceStart = totalDays < 0 ? -1 : (int)Math.Floor(totalDays);
        var nextIndex = daysSinceStart + 1; // next day index (0-based) after the 'afterUtc'
        // align to interval
        if (nextIndex % Interval != 0)
            nextIndex += (Interval - (nextIndex % Interval));
        var next = first.AddDays(nextIndex);
        return next >= deadlineUtc ? null : next;
    }

    private DateTime? NextWeekly(DateTime afterUtc, DateTime deadlineUtc, DateTime first)
    {
        // Iterate day by day until we find a valid weekday in an eligible week based on Interval.
        var cursor = afterUtc.Date.AddDays(1); // start checking from next day
        var maxScan = 400; // safety cap
        while (maxScan-- > 0)
        {
            var candidateDateTime = new DateTime(cursor.Year, cursor.Month, cursor.Day, TimeOfDay.Hour, TimeOfDay.Minute, TimeOfDay.Second, DateTimeKind.Utc);
            if (candidateDateTime >= deadlineUtc) return null;
            var weeksFromStart = WeeksBetween(first.Date, cursor);
            if (weeksFromStart >= 0 && weeksFromStart % Interval == 0)
            {
                var mask = DayOfWeekToMask(cursor.DayOfWeek);
                if ((Weekdays & mask) != 0 && candidateDateTime > afterUtc)
                    return candidateDateTime;
            }
            cursor = cursor.AddDays(1);
        }
        return null;
    }

    private static int WeeksBetween(DateOnly start, DateOnly current)
    {
        var diffDays = current.DayNumber - start.DayNumber;
        return diffDays < 0 ? -1 : diffDays / 7;
    }
    private static int WeeksBetween(DateTime start, DateTime current) => WeeksBetween(DateOnly.FromDateTime(start), DateOnly.FromDateTime(current));

    private DateTime? NextMonthly(DateTime afterUtc, DateTime deadlineUtc, DateTime first)
    {
        // Determine month steps from start
        var baseMonthStart = new DateTime(StartDate.Year, StartDate.Month, 1, TimeOfDay.Hour, TimeOfDay.Minute, TimeOfDay.Second, DateTimeKind.Utc);
        // Starting from month of 'afterUtc'
        var cursorMonth = new DateTime(afterUtc.Year, afterUtc.Month, 1, TimeOfDay.Hour, TimeOfDay.Minute, TimeOfDay.Second, DateTimeKind.Utc);

        // Ensure we start at next day after 'afterUtc'
        cursorMonth = cursorMonth.AddMonths(0);
        for (int i = 0; i < 60; i++) // scan up to 5 years
        {
            var monthsFromStart = ((cursorMonth.Year - baseMonthStart.Year) * 12) + (cursorMonth.Month - baseMonthStart.Month);
            if (monthsFromStart >= 0 && monthsFromStart % Interval == 0)
            {
                DateTime? candidate = null;
                if (MonthDay != null)
                {
                    var day = MonthDay.Value;
                    var daysIn = DateTime.DaysInMonth(cursorMonth.Year, cursorMonth.Month);
                    if (day > daysIn) day = daysIn;
                    candidate = new DateTime(cursorMonth.Year, cursorMonth.Month, day, TimeOfDay.Hour, TimeOfDay.Minute, TimeOfDay.Second, DateTimeKind.Utc);
                }
                else if (NthWeek != null && NthWeekday != null)
                {
                    candidate = ComputeNthWeekday(cursorMonth.Year, cursorMonth.Month, NthWeek.Value, NthWeekday.Value, TimeOfDay);
                }

                if (candidate != null && candidate > afterUtc)
                    return candidate >= deadlineUtc ? null : candidate;
            }
            cursorMonth = cursorMonth.AddMonths(1);
        }
        return null;
    }

    private static DateTime? ComputeNthWeekday(int year, int month, int nthWeek, int nthWeekday, TimeOnly time)
    {
        if (nthWeek < 1 || nthWeek > 5) return null; // later: support -1 for last
        if (nthWeekday < 0 || nthWeekday > 6) return null;
        var firstOfMonth = new DateTime(year, month, 1, time.Hour, time.Minute, time.Second, DateTimeKind.Utc);
        var firstDow = (int)firstOfMonth.DayOfWeek; // 0=Sunday .. 6=Saturday
        // Convert to Monday=0 .. Sunday=6 mapping we used earlier (NthWeekday 0=Mon)
        var desiredDow = (nthWeekday + 1) % 7; // convert Mon(0)->1, ..., Sun(6)->0 for DayOfWeek
        int offset = desiredDow - firstDow;
        if (offset < 0) offset += 7;
        var day = 1 + offset + (nthWeek - 1) * 7;
        if (day > DateTime.DaysInMonth(year, month)) return null;
        return new DateTime(year, month, day, time.Hour, time.Minute, time.Second, DateTimeKind.Utc);
    }

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
