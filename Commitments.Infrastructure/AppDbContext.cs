using Commitments.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Commitments.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Commitment> Commitments => Set<Commitment>();
    public DbSet<Schedule> Schedules => Set<Schedule>();
    public DbSet<CheckIn> CheckIns => Set<CheckIn>();
    public DbSet<PaymentIntentLog> PaymentIntentLogs => Set<PaymentIntentLog>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ReminderEvent> ReminderEvents => Set<ReminderEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Commitment>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Goal).HasMaxLength(200).IsRequired();
            e.HasOne(x => x.Schedule).WithOne().HasForeignKey<Schedule>(s => s.CommitmentId);
            e.HasMany(x => x.CheckIns).WithOne().HasForeignKey(ci => ci.CommitmentId);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => new { x.Status, x.DeadlineUtc });
        });

        b.Entity<Schedule>(e =>
        {
            e.HasKey(x => x.CommitmentId);
        });

        b.Entity<CheckIn>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CommitmentId);
        });

        b.Entity<PaymentIntentLog>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CommitmentId);
            e.Property(x => x.StripePaymentIntentId).HasMaxLength(200);
        });

        b.Entity<AuditLog>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CommitmentId);
            e.HasIndex(x => x.EventType);
        });

        b.Entity<ReminderEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.CommitmentId, x.ScheduledForUtc });
            e.HasIndex(x => x.Status);
        });
    }
}
