using Commitments.Domain.Entities;
using Commitments.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Commitments.Domain.Abstractions;

namespace Commitments.Api.Notifications;

public interface INotificationSender
{
    Task SendAsync(Guid userId, string channel, string subject, string body, CancellationToken ct = default);
}

public class ConsoleNotificationSender : INotificationSender
{
    public Task SendAsync(Guid userId, string channel, string subject, string body, CancellationToken ct = default)
    {
        Console.WriteLine($"[NOTIFY] user={userId} channel={channel} subject={subject} body={body}");
        return Task.CompletedTask;
    }
}

public class InMemoryNotificationSender : INotificationSender
{
    public List<(Guid userId,string channel,string subject,string body)> Sent { get; } = new();
    public Task SendAsync(Guid userId, string channel, string subject, string body, CancellationToken ct = default)
    {
        Sent.Add((userId,channel,subject,body));
        return Task.CompletedTask;
    }
}

public interface IReminderNotificationDispatcher
{
    Task DispatchAsync(CancellationToken ct = default);
}

public class ReminderNotificationDispatcher(AppDbContext db, INotificationSender sender, IClock clock) : IReminderNotificationDispatcher
{
    private const int DefaultQuietStartHour = 22; // 22:00
    private const int DefaultQuietEndHour = 7;   // 07:00
    private const int MaxDeferrals = 3; // safety: after 3 quiet deferrals send anyway

    public async Task DispatchAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var reminders = await db.ReminderEvents
            .Where(r => r.Status == "pending" && r.ScheduledForUtc <= now)
            .OrderBy(r => r.ScheduledForUtc)
            .Take(200)
            .ToListAsync(ct);
        if (reminders.Count == 0) return;

        var commitmentIds = reminders.Select(r => r.CommitmentId).Distinct().ToList();
        var commitments = await db.Commitments.Where(c => commitmentIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);

        foreach (var rem in reminders)
        {
            if (!commitments.TryGetValue(rem.CommitmentId, out var commitment))
            {
                rem.Status = "skipped";
                rem.ProcessedAtUtc = now;
                continue;
            }

            // Derive local time (placeholder: treat timezone as UTC)
            var localNow = now; // TODO real TZ conversion
            var (startHour, endHour) = GetQuietWindow(commitment.UserId); // future per-user customization
            var inQuiet = IsInQuiet(localNow, startHour, endHour);

            if (inQuiet && GetDeferralCount(rem) < MaxDeferrals)
            {
                var nextBoundary = ComputeNextQuietEnd(localNow, startHour, endHour);
                rem.ScheduledForUtc = nextBoundary;
                SetDeferralCount(rem, GetDeferralCount(rem) + 1);
                continue;
            }

            await sender.SendAsync(commitment.UserId, "console", rem.Type, $"Reminder for commitment {commitment.Goal}", ct);
            rem.Status = "sent";
            rem.ProcessedAtUtc = now;
        }
        await db.SaveChangesAsync(ct);
    }

    private static (int startHour, int endHour) GetQuietWindow(Guid userId)
        => (DefaultQuietStartHour, DefaultQuietEndHour);

    private static bool IsInQuiet(DateTime local, int startHour, int endHour)
    {
        if (startHour < endHour)
        {
            // Simple same-day window (not used here but future-proof)
            return local.Hour >= startHour && local.Hour < endHour;
        }
        // Overnight window (e.g., 22 -> 7)
        if (local.Hour >= startHour) return true; // 22..23
        if (local.Hour < endHour) return true;    // 0..6
        return false;
    }

    private static DateTime ComputeNextQuietEnd(DateTime local, int startHour, int endHour)
    {
        if (startHour < endHour)
        {
            var endToday = new DateTime(local.Year, local.Month, local.Day, endHour, 0, 0, DateTimeKind.Utc);
            if (local < endToday) return endToday;
            return endToday.AddDays(1);
        }
        // overnight window
        if (local.Hour >= startHour)
        {
            // send at endHour next day
            var nextDayEnd = new DateTime(local.Year, local.Month, local.Day, endHour, 0, 0, DateTimeKind.Utc).AddDays(1);
            return nextDayEnd;
        }
        // before end hour same morning
        return new DateTime(local.Year, local.Month, local.Day, endHour, 0, 0, DateTimeKind.Utc);
    }

    // Store deferral count encoded in Type suffix (simple, avoids schema change) e.g., originalType|d=2
    private static int GetDeferralCount(ReminderEvent rem)
    {
        var parts = rem.Type.Split('|');
        foreach (var p in parts)
        {
            if (p.StartsWith("d=", StringComparison.OrdinalIgnoreCase) && int.TryParse(p[2..], out var n))
                return n;
        }
        return 0;
    }

    private static void SetDeferralCount(ReminderEvent rem, int count)
    {
        var baseType = rem.Type.Split('|')[0];
        rem.Type = count == 0 ? baseType : $"{baseType}|d={count}";
    }
}

public class ReminderNotificationJob(IReminderNotificationDispatcher dispatcher)
{
    public Task RunAsync() => dispatcher.DispatchAsync();
}
