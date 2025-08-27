using Commitments.Domain.Entities;
using Commitments.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Commitments.Domain.Abstractions;

namespace Commitments.Api.Background;

public interface IGraceExpiryScanner
{
    Task ScanAsync(CancellationToken ct = default);
}

public class GraceExpiryScanner(AppDbContext db, IClock clock) : IGraceExpiryScanner
{
    private static readonly TimeSpan GraceWindow = TimeSpan.FromMinutes(60); // configurable later

    public async Task ScanAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        // Transition Active -> DecisionNeeded at deadline
        var due = await db.Commitments.Where(c => c.Status == CommitmentStatus.Active && c.DeadlineUtc <= now).ToListAsync(ct);
        foreach (var c in due)
        {
            try
            {
                c.TransitionToDecisionNeeded(GraceWindow);
                // add reminder event for final warning 15m before grace expiry if possible
                var finalWarnAt = c.GraceExpiresUtc!.Value.AddMinutes(-15);
                if (finalWarnAt > now)
                {
                    db.ReminderEvents.Add(new ReminderEvent
                    {
                        CommitmentId = c.Id,
                        ScheduledForUtc = finalWarnAt,
                        Type = "commitment.grace_final_warning"
                    });
                }
            }
            catch { /* ignore invalid */ }
        }

        // Auto-fail expired grace
        var expired = await db.Commitments.Where(c => c.Status == CommitmentStatus.DecisionNeeded && c.GraceExpiresUtc != null && c.GraceExpiresUtc <= now).ToListAsync(ct);
        foreach (var c in expired)
        {
            try { c.Fail(); } catch { }
        }

        if (due.Count > 0 || expired.Count > 0)
            await db.SaveChangesAsync(ct);
    }
}

public class GraceExpiryJob(IGraceExpiryScanner scanner)
{
    public Task RunAsync() => scanner.ScanAsync();
}
