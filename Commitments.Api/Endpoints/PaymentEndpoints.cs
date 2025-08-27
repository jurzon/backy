using Commitments.Api.Payments;
using Commitments.Domain.Entities;
using Commitments.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Commitments.Api.Endpoints;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/payments").RequireAuthorization();

        group.MapGet("/setup/{userId:guid}", async (Guid userId, bool ensure, IPaymentService paymentService, AppDbContext db, CancellationToken ct) =>
        {
            if (ensure)
            {
                await paymentService.EnsureSetupIntentAsync(userId, ct);
            }
            var state = await db.PaymentSetupStates.FirstOrDefaultAsync(s => s.UserId == userId, ct);
            if (state == null)
            {
                return Results.NotFound();
            }
            return Results.Ok(new PaymentSetupStatusResponse(state.UserId, state.HasPaymentMethod, state.LatestSetupIntentId, state.UpdatedAtUtc));
        }).WithName("GetPaymentSetupStatus").WithOpenApi();

        group.MapPost("/setup/{userId:guid}/ensure", async (Guid userId, IPaymentService paymentService, AppDbContext db, CancellationToken ct) =>
        {
            await paymentService.EnsureSetupIntentAsync(userId, ct);
            var state = await db.PaymentSetupStates.FirstAsync(s => s.UserId == userId, ct);
            return Results.Ok(new PaymentSetupStatusResponse(state.UserId, state.HasPaymentMethod, state.LatestSetupIntentId, state.UpdatedAtUtc));
        }).WithName("EnsurePaymentSetup").WithOpenApi();

        return app;
    }
}

public sealed record PaymentSetupStatusResponse(Guid UserId, bool HasPaymentMethod, string? LatestSetupIntentId, DateTime UpdatedAtUtc);
