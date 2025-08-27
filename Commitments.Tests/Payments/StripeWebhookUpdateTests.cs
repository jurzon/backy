using System.Text;
using System.Text.Json;
using Commitments.Domain.Entities;
using Commitments.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Commitments.Tests.Payments;

public class WebhookUpdateApiFactory : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(cfg =>
        {
            // Empty secret disables signature verification for simpler tests
            cfg.AddInMemoryCollection(new Dictionary<string,string?>
            {
                ["Stripe:WebhookSecret"] = string.Empty
            });
        });
        builder.UseEnvironment("Development");
        return base.CreateHost(builder);
    }
}

public class StripeWebhookUpdateTests : IClassFixture<WebhookUpdateApiFactory>
{
    private readonly WebhookUpdateApiFactory _factory;
    public StripeWebhookUpdateTests(WebhookUpdateApiFactory factory) => _factory = factory;

    [Fact]
    public async Task UpdatesStatusToSucceeded()
    {
        var client = _factory.CreateClient();
        var paymentIntentId = "pi_succ_123";
        await SeedLogAsync(paymentIntentId, status: "created");

        var payload = BuildEvent("payment_intent.succeeded", paymentIntentId, status: "succeeded");
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var resp = await client.PostAsync("/webhooks/stripe", content);
        resp.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = await db.PaymentIntentLogs.FirstAsync(l => l.StripePaymentIntentId == paymentIntentId);
        log.Status.Should().Be("succeeded");
        log.LastErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task UpdatesStatusToFailedAndStoresError()
    {
        var client = _factory.CreateClient();
        var paymentIntentId = "pi_fail_123";
        await SeedLogAsync(paymentIntentId, status: "created");

        var payload = BuildEvent("payment_intent.payment_failed", paymentIntentId, status: "requires_payment_method", errorCode: "card_declined");
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var resp = await client.PostAsync("/webhooks/stripe", content);
        resp.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = await db.PaymentIntentLogs.FirstAsync(l => l.StripePaymentIntentId == paymentIntentId);
        log.Status.Should().Be("failed");
        log.LastErrorCode.Should().Be("card_declined");
    }

    private static string BuildEvent(string type, string paymentIntentId, string status, string? errorCode = null)
    {
        var root = new Dictionary<string, object?>
        {
            ["id"] = $"evt_{Guid.NewGuid():N}",
            ["object"] = "event",
            ["created"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["type"] = type,
            ["data"] = new Dictionary<string, object?>
            {
                ["object"] = new Dictionary<string, object?>
                {
                    ["id"] = paymentIntentId,
                    ["object"] = "payment_intent",
                    ["status"] = status,
                    ["last_payment_error"] = errorCode == null ? null : new Dictionary<string, object?> { ["code"] = errorCode }
                }
            }
        };
        return JsonSerializer.Serialize(root);
    }

    private async Task SeedLogAsync(string paymentIntentId, string status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await db.PaymentIntentLogs.AnyAsync(l => l.StripePaymentIntentId == paymentIntentId))
        {
            db.PaymentIntentLogs.Add(new PaymentIntentLog
            {
                StripePaymentIntentId = paymentIntentId,
                CommitmentId = Guid.NewGuid(),
                AmountMinor = 1000,
                Currency = "eur",
                Status = status,
                AttemptNumber = 1,
            });
            await db.SaveChangesAsync();
        }
    }
}
