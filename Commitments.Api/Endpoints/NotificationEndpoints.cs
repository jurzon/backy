using Commitments.Infrastructure;
using Commitments.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;

namespace Commitments.Api.Endpoints;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/notifications").RequireAuthorization();

        g.MapGet("/quiet-hours/{userId:guid}", async (Guid userId, AppDbContext db, CancellationToken ct) =>
        {
            var q = await db.NotificationQuietHours.FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (q == null) return Results.NotFound();
            return Results.Ok(new QuietHoursResponse(q.UserId, q.StartHour, q.EndHour, q.Timezone, q.UpdatedAtUtc));
        }).WithOpenApi();

        g.MapPost("/quiet-hours/{userId:guid}", async (Guid userId, QuietHoursRequest req, AppDbContext db, CancellationToken ct) =>
        {
            if (req.StartHour < 0 || req.StartHour > 23 || req.EndHour < 0 || req.EndHour > 23)
                return Results.BadRequest("Hours 0-23");
            var existing = await db.NotificationQuietHours.FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (existing == null)
            {
                existing = new NotificationQuietHours { UserId = userId };
                db.NotificationQuietHours.Add(existing);
            }
            existing.StartHour = req.StartHour;
            existing.EndHour = req.EndHour;
            existing.Timezone = string.IsNullOrWhiteSpace(req.Timezone) ? "UTC" : req.Timezone;
            existing.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new QuietHoursResponse(existing.UserId, existing.StartHour, existing.EndHour, existing.Timezone, existing.UpdatedAtUtc));
        }).WithOpenApi();

        return app;
    }
}

public sealed record QuietHoursRequest(int StartHour, int EndHour, string Timezone);
public sealed record QuietHoursResponse(Guid UserId, int StartHour, int EndHour, string Timezone, DateTime UpdatedAtUtc);
