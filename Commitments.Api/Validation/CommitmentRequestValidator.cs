using Commitments.Api.Endpoints;
using System.Text.RegularExpressions;

namespace Commitments.Api.Validation;

public static class CommitmentRequestValidator
{
    private static readonly HashSet<string> AllowedCurrencies = new(["EUR","USD","CHF","PLN","CZK","HUF"]);

    public static Dictionary<string, string[]> Validate(CreateCommitmentRequest req)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var arr)) errors[field] = new[] { message };
            else errors[field] = arr.Concat(new[] { message }).ToArray();
        }
        if (string.IsNullOrWhiteSpace(req.Goal) || req.Goal.Length > 200) Add("goal", "Goal length 1-200 required");
        if (req.StakeAmount <= 0) Add("stakeAmount", "Stake must be > 0");
        if (req.DeadlineUtc <= DateTime.UtcNow.AddHours(1)) Add("deadlineUtc", "Deadline must be at least 1h in future");
        if (string.IsNullOrWhiteSpace(req.Currency)) Add("currency", "Currency required");
        else
        {
            var ccy = req.Currency.ToUpperInvariant();
            if (!Regex.IsMatch(ccy, "^[A-Z]{3}$")) Add("currency", "Invalid format");
            else if (!AllowedCurrencies.Contains(ccy)) Add("currency", "Unsupported currency");
        }
        if (req.Schedule is null) Add("schedule", "Schedule required");
        else ValidateSchedule(req.Schedule, Add);
        return errors;
    }

    private static void ValidateSchedule(ScheduleDto s, Action<string,string> add)
    {
        if (s.Interval < 1) add("schedule.interval", "Interval must be >=1");
        if (s.TimeOfDay == default) add("schedule.timeOfDay", "TimeOfDay required");
        if (s.StartDate == default) add("schedule.startDate", "StartDate required");
        switch (s.PatternType.ToLowerInvariant())
        {
            case "daily":
                break;
            case "weekly":
                if (string.IsNullOrWhiteSpace(s.WeekdaysMask)) add("schedule.weekdaysMask", "WeekdaysMask required for weekly");
                break;
            case "monthly_day":
                if (s.MonthDay is null or < 1 or > 31) add("schedule.monthDay", "MonthDay 1-31 required");
                break;
            case "monthly_nth":
                if (s.NthWeek is null or < 1 or > 5) add("schedule.nthWeek", "NthWeek 1-5 required");
                if (s.NthWeekday is null or < 0 or > 6) add("schedule.nthWeekday", "NthWeekday 0-6 required");
                break;
            default:
                add("schedule.patternType", "Unsupported patternType");
                break;
        }
    }
}
