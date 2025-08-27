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
        var userIds = commitments.Values.Select(c => c.UserId).Distinct().ToList();
        var quiet = await db.NotificationQuietHours.Where(q => userIds.Contains(q.UserId)).ToDictionaryAsync(q => q.UserId, ct);

        foreach (var rem in reminders)
        {
            if (!commitments.TryGetValue(rem.CommitmentId, out var commitment))
            {
                rem.Status = "skipped";
                rem.ProcessedAtUtc = now;
                continue;
            }

            var localNow = now; // TODO timezone conversion using commitment.Timezone
            var (startHour, endHour) = quiet.TryGetValue(commitment.UserId, out var q)
                ? (q.StartHour, q.EndHour)
                : (22, 7); // default

            var inQuiet = IsInQuiet(localNow, startHour, endHour);
            if (inQuiet && GetDeferralCount(rem) < MaxDeferrals)
            {
                rem.ScheduledForUtc = ComputeNextQuietEnd(localNow, startHour, endHour);
                SetDeferralCount(rem, GetDeferralCount(rem) + 1);
                continue;
            }

            await sender.SendAsync(commitment.UserId, "console", rem.Type, $"Reminder for commitment {commitment.Goal}", ct);
            rem.Status = "sent";
            rem.ProcessedAtUtc = now;
        }
        await db.SaveChangesAsync(ct);
    }

    private static bool IsInQuiet(DateTime local, int startHour, int endHour)
    {
        if (startHour == endHour) return false; // zero window
        if (startHour < endHour)
            return local.Hour >= startHour && local.Hour < endHour;
        if (local.Hour >= startHour) return true;
        if (local.Hour < endHour) return true;
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
        if (local.Hour >= startHour)
            return new DateTime(local.Year, local.Month, local.Day, endHour, 0, 0, DateTimeKind.Utc).AddDays(1);
        return new DateTime(local.Year, local.Month, local.Day, endHour, 0, 0, DateTimeKind.Utc);
    }

    private static int GetDeferralCount(ReminderEvent rem)
    {
        var parts = rem.Type.Split('|');
        foreach (var p in parts)
            if (p.StartsWith("d=", StringComparison.OrdinalIgnoreCase) && int.TryParse(p[2..], out var n)) return n;
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
