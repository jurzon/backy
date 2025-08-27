using Commitments.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Hangfire;
using Hangfire.PostgreSql;
using Commitments.Api.Background;
using Commitments.Domain.Abstractions;
using Commitments.Api.Payments;
using Stripe;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var conn = builder.Configuration.GetConnectionString("Postgres") ?? "Host=localhost;Username=commitments;Password=commitments;Database=commitments";
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(conn));

// Stripe API Key (dev: can be empty placeholder)
var stripeKey = builder.Configuration.GetValue<string>("Stripe:ApiKey");
if (!string.IsNullOrWhiteSpace(stripeKey)) StripeConfiguration.ApiKey = stripeKey;

// Hangfire configuration
builder.Services.AddHangfire(config =>
    config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
          .UseSimpleAssemblyNameTypeSerializer()
          .UseRecommendedSerializerSettings()
          .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(conn)));

builder.Services.AddHangfireServer();

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<IReminderScheduler, ReminderScheduler>();
builder.Services.AddScoped<IGraceExpiryScanner, GraceExpiryScanner>();
builder.Services.AddScoped<IPaymentService, StripePaymentService>();

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

app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

app.MapCommitmentEndpoints();

// Payments placeholder endpoint (webhook stub)
app.MapPost("/webhooks/stripe", () => Results.Ok());

// Recurring jobs
RecurringJob.AddOrUpdate<ReminderHorizonJob>("reminder-horizon", job => job.RunAsync(), "*/15 * * * *");
RecurringJob.AddOrUpdate<GraceExpiryJob>("grace-expiry", job => job.RunAsync(), "*/5 * * * *");

app.Run();
