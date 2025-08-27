using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Commitments.Tests.Payments;

public class TestApiFactory : WebApplicationFactory<Program>
{
    internal const string TestSecret = "whsec_test";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(cfg =>
        {
            var dict = new Dictionary<string, string?>
            {
                ["Stripe:WebhookSecret"] = TestSecret
            };
            cfg.AddInMemoryCollection(dict);
        });
        builder.UseEnvironment("Development");
        return base.CreateHost(builder);
    }
}

public class StripeWebhookTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public StripeWebhookTests(TestApiFactory factory) => _factory = factory;

    private static string GenerateSignature(string payload, string secret, long? ts = null)
    {
        var timestamp = ts ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var toSign = $"{timestamp}.{payload}";
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var sig = hmac.ComputeHash(Encoding.UTF8.GetBytes(toSign));
        var hex = string.Concat(sig.Select(b => b.ToString("x2")));
        return $"t={timestamp},v1={hex}";
    }

    private static string BuildEventJson(string type, string paymentIntentId)
    {
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return $"{{\"id\":\"evt_{Guid.NewGuid():N}\",\"object\":\"event\",\"created\":{created},\"type\":\"{type}\",\"data\":{{\"object\":{{\"id\":\"{paymentIntentId}\",\"object\":\"payment_intent\",\"status\":\"succeeded\"}}}}}}";
    }

    [Fact]
    public async Task RejectsInvalidSignature()
    {
        var client = _factory.CreateClient();
        var payload = BuildEventJson("payment_intent.succeeded", "pi_123");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/stripe")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature", GenerateSignature(payload + "tamper", TestApiFactory.TestSecret));
        var resp = await client.SendAsync(request);
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AcceptsEventWhenSignatureVerificationDisabled()
    {
        // Override configuration to disable signature enforcement
        var factoryNoSig = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration(cfg =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?> { ["Stripe:WebhookSecret"] = string.Empty, } )));
        var client = factoryNoSig.CreateClient();
        var payload = BuildEventJson("payment_intent.succeeded", "pi_456");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/stripe")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        var resp = await client.SendAsync(request);
        resp.EnsureSuccessStatusCode();
    }
}
