using Commitments.Domain.Entities;

namespace Commitments.Api.Endpoints;

public record CreateCommitmentRequest(
    Guid UserId,
    string Goal,
    decimal StakeAmount,
    string Currency,
    DateTime DeadlineUtc,
    string Timezone,
    ScheduleDto Schedule
);

public record ScheduleDto(
    string PatternType,
    int Interval,
    string? WeekdaysMask,
    int? MonthDay,
    int? NthWeek,
    int? NthWeekday,
    DateOnly StartDate,
    TimeOnly TimeOfDay
);

public record CommitmentSummaryResponse(
    Guid Id,
    string Goal,
    string Currency,
    long StakeAmountMinor,
    DateTime DeadlineUtc,
    string Status,
    double ProgressPercent,
    string RiskBadge
);

public record CreateCheckInRequest(string? Note, string? PhotoUrl);
public record CheckInResponse(Guid Id, DateTime OccurredAtUtc, string? Note, string? PhotoUrl);

public static class CommitmentMappings
{
    public static CommitmentSummaryResponse ToSummary(this Commitment c)
    {
        var now = DateTime.UtcNow;
        var total = (c.DeadlineUtc - c.CreatedAtUtc).TotalSeconds;
        var elapsed = Math.Clamp((now - c.CreatedAtUtc).TotalSeconds, 0, total <= 0 ? 1 : total);
        var progress = total <= 0 ? 100 : Math.Min(100, (elapsed / total) * 100);
        var risk = c.ComputeRiskBadge(now);
        return new CommitmentSummaryResponse(
            c.Id,
            c.Goal,
            c.Currency,
            c.StakeAmountMinor,
            c.DeadlineUtc,
            c.Status.ToString(),
            Math.Round(progress, 2),
            risk
        );
    }

    public static CheckInResponse ToResponse(this CheckIn ci) => new(ci.Id, ci.OccurredAtUtc, ci.Note, ci.PhotoUrl);
}
