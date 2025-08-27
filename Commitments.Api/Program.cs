using Commitments.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Hangfire;
using Hangfire.PostgreSql;
using Commitments.Api.Background;
using Commitments.Domain.Abstractions;
using Commitments.Api.Payments;
using Commitments.Api.Notifications;
using Stripe;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Commitments API", Version = "v1" });
    c.AddSecurityDefinition("basic", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "basic",
        Description = "Basic authentication (dev only)"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "basic" } }, Array.Empty<string>() }
    });
});

builder.Services.AddAuthentication("Basic").AddScheme<AuthenticationSchemeOptions, BasicAuthHandler>("Basic", _ => { });
builder.Services.AddAuthorization();

var conn = builder.Configuration.GetConnectionString("Postgres") ?? "Host=localhost;Username=commitments;Password=commitments;Database=commitments";
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(conn));

// Stripe API Key (dev: can be empty placeholder)
var stripeKey = builder.Configuration.GetValue<string>("Stripe:ApiKey");
var stripeWebhookSecret = builder.Configuration.GetValue<string>("Stripe:WebhookSecret");
if (!string.IsNullOrWhiteSpace(stripeKey)) StripeConfiguration.ApiKey = stripeKey;

// Hangfire configuration
builder.Services.AddHangfire(config =>
    config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
          .UseSimpleAssemblyNameTypeSerializer()
          .UseRecommendedSerializerSettings()
          .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(conn)));

builder.Services.AddHangfireServer();

builder.Services.AddSingleton<IClock, Commitments.Domain.Abstractions.SystemClock>();
builder.Services.AddScoped<IReminderScheduler, ReminderScheduler>();
builder.Services.AddScoped<IGraceExpiryScanner, GraceExpiryScanner>();
builder.Services.AddScoped<IPaymentService, StripePaymentService>();
builder.Services.AddScoped<IPaymentRetryWorker, PaymentRetryWorker>();
// Notifications
builder.Services.AddScoped<INotificationSender, ConsoleNotificationSender>();
builder.Services.AddScoped<IReminderNotificationDispatcher, ReminderNotificationDispatcher>();

var app = builder.Build();

// Auto-migrate
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseHangfireDashboard("/hangfire");
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", [AllowAnonymous]() => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

app.MapCommitmentEndpoints();

// Payments placeholder endpoint (webhook stub)
app.MapPost("/webhooks/stripe", [AllowAnonymous] async (HttpRequest request, IPaymentService payments) =>
{
    var json = await new StreamReader(request.Body).ReadToEndAsync();
    try
    {
        Event stripeEvent;
        if (!string.IsNullOrWhiteSpace(stripeWebhookSecret))
        {
            var sigHeader = request.Headers["Stripe-Signature"].ToString();
            stripeEvent = EventUtility.ConstructEvent(json, sigHeader, stripeWebhookSecret);
        }
        else
        {
            stripeEvent = EventUtility.ParseEvent(json);
        }

        switch (stripeEvent.Type)
        {
            case "payment_intent.succeeded":
                if (stripeEvent.Data.Object is PaymentIntent pis)
                    await payments.UpdatePaymentStatusAsync(pis.Id, "succeeded");
                break;
            case "payment_intent.payment_failed":
                if (stripeEvent.Data.Object is PaymentIntent pif)
                    await payments.UpdatePaymentStatusAsync(pif.Id, "failed", pif.LastPaymentError?.Code);
                break;
        }
        return Results.Ok();
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: 400);
    }
}).WithName("StripeWebhook").WithOpenApi();

// Recurring jobs
RecurringJob.AddOrUpdate<ReminderHorizonJob>("reminder-horizon", job => job.RunAsync(), "*/15 * * * *");
RecurringJob.AddOrUpdate<GraceExpiryJob>("grace-expiry", job => job.RunAsync(), "*/5 * * * *");
RecurringJob.AddOrUpdate("payment-retry", () => PaymentRetryJob.Run(app.Services), Cron.Daily);
RecurringJob.AddOrUpdate<ReminderNotificationJob>("reminder-dispatch", job => job.RunAsync(), "*/10 * * * *");

app.Run();

public static class PaymentRetryJob
{
    public static async Task Run(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var worker = scope.ServiceProvider.GetRequiredService<IPaymentRetryWorker>();
        await worker.RunAsync();
    }
}

// Basic dev-only authentication handler
public class BasicAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private static readonly string DevUser = "dev";
    private static readonly string DevPassword = "dev";
    private static readonly Guid DevUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public BasicAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock)
        : base(options, logger, encoder, clock) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
            return Task.FromResult(AuthenticateResult.NoResult());
        try
        {
            var header = Request.Headers["Authorization"].ToString();
            if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(AuthenticateResult.Fail("Invalid scheme"));
            var encoded = header.Substring("Basic ".Length).Trim();
            var creds = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var parts = creds.Split(':');
            if (parts.Length != 2) return Task.FromResult(AuthenticateResult.Fail("Invalid basic token"));
            if (parts[0] == DevUser && parts[1] == DevPassword)
            {
                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, DevUserId.ToString()),
                    new Claim(ClaimTypes.Name, DevUser)
                };
                var identity = new ClaimsIdentity(claims, Scheme.Name);
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, Scheme.Name);
                return Task.FromResult(AuthenticateResult.Success(ticket));
            }
            return Task.FromResult(AuthenticateResult.Fail("Bad credentials"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(AuthenticateResult.Fail(ex));
        }
    }
}
