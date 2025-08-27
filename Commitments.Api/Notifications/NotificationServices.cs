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
    private const int QuietStartHour = 22; // 22:00
    private const int QuietEndHour = 7;   // 07:00 next day

    public async Task DispatchAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var reminders = await db.ReminderEvents
            .Where(r => r.Status == "pending" && r.ScheduledForUtc <= now)
            .OrderBy(r => r.ScheduledForUtc)
            .Take(100)
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
            var tz = commitment.Timezone ?? "UTC"; // timezone placeholder; treat as UTC until TZ conversion implemented
            // For now we interpret quiet hours in commitment timezone treated as UTC.
            var local = now; // TODO convert using TZ database
            var inQuiet = IsInQuiet(local);
            if (inQuiet)
            {
                // defer until quiet end
                var quietEndToday = new DateTime(local.Year, local.Month, local.Day, QuietEndHour, 0,0, DateTimeKind.Utc);
                if (local.Hour >= QuietStartHour) // after start -> quiet end next day
                    quietEndToday = quietEndToday.AddDays(1);
                rem.ScheduledForUtc = quietEndToday;
                // keep status pending
                continue;
            }
            await sender.SendAsync(commitment.UserId, "console", rem.Type, $"Reminder for commitment {commitment.Goal}", ct);
            rem.Status = "sent";
            rem.ProcessedAtUtc = now;
        }
        await db.SaveChangesAsync(ct);
    }

    private static bool IsInQuiet(DateTime local)
    {
        if (local.Hour >= QuietStartHour) return true; // 22:00 -> 23:59
        if (local.Hour < QuietEndHour) return true;    // 00:00 -> 06:59
        return false;
    }
}

public class ReminderNotificationJob(IReminderNotificationDispatcher dispatcher)
{
    public Task RunAsync() => dispatcher.DispatchAsync();
}
