using Commitments.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var conn = builder.Configuration.GetConnectionString("Postgres") ?? "Host=localhost;Username=commitments;Password=commitments;Database=commitments";
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(conn));

var app = builder.Build();

// Auto-migrate database on startup (idempotent). Consider wrapping with retry for container orchestration.
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Database migration failed: {ex.Message}");
        throw; // Fail fast so container can restart
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

app.MapCommitmentEndpoints();

app.Run();
