using Commitments.Api.Payments;
using Commitments.Domain.Entities;
using Commitments.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Commitments.Tests.Payments;

public class PaymentRetryIntegrationTests
{
    [Fact]
    public async Task RetryWorkerProcessesMultipleEligibleLogs()
    {
        using var db = CreateDb();
        var c1 = Guid.NewGuid();
        var c2 = Guid.NewGuid();
        db.PaymentIntentLogs.AddRange(
            FailedLog(c1, 1, updatedHoursAgo: 30),
            FailedLog(c2, 2, updatedHoursAgo: 50),
            FailedLog(Guid.NewGuid(), 1, updatedHoursAgo: 5) // too recent
        );
        await db.SaveChangesAsync();
        var worker = new PaymentRetryWorker(db, new DummyPaymentService(), NullLogger<PaymentRetryWorker>.Instance);

        await worker.RunAsync();

        var processed = await db.PaymentIntentLogs.Where(l => l.CommitmentId == c1 || l.CommitmentId == c2).ToListAsync();
        processed.Should().OnlyContain(l => l.Status == "succeeded" && l.AttemptNumber >= 2);
        var skippedRecent = await db.PaymentIntentLogs.FirstAsync(l => l.AttemptNumber == 1 && l.CommitmentId != c1 && l.CommitmentId != c2);
        skippedRecent.Status.Should().Be("failed");
    }

    [Fact]
    public async Task HardDeclineNotRetried()
    {
        using var db = CreateDb();
        var c1 = Guid.NewGuid();
        db.PaymentIntentLogs.Add(FailedLog(c1, 1, updatedHoursAgo: 30, errorCode: "card_declined"));
        await db.SaveChangesAsync();
        var worker = new PaymentRetryWorker(db, new DummyPaymentService(), NullLogger<PaymentRetryWorker>.Instance);

        await worker.RunAsync();

        var log = await db.PaymentIntentLogs.FirstAsync();
        log.Status.Should().Be("failed");
        log.AttemptNumber.Should().Be(1);
    }

    private static AppDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(opts);
    }

    private static PaymentIntentLog FailedLog(Guid commitmentId, int attempt, int updatedHoursAgo, string? errorCode = null)
        => new()
        {
            CommitmentId = commitmentId,
            StripePaymentIntentId = $"pi_{Guid.NewGuid():N}",
            AmountMinor = 1000,
            Currency = "eur",
            Status = "failed",
            AttemptNumber = attempt,
            LastErrorCode = errorCode,
            UpdatedAtUtc = DateTime.UtcNow.AddHours(-updatedHoursAgo)
        };

    private sealed class DummyPaymentService : IPaymentService
    {
        public Task<PaymentIntentLog> CreateFailurePaymentIntentAsync(Commitment commitment, CancellationToken ct = default) => Task.FromResult(new PaymentIntentLog());
        public Task EnsureSetupIntentAsync(Guid userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdatePaymentStatusAsync(string paymentIntentId, string newStatus, string? errorCode = null, CancellationToken ct = default) => Task.CompletedTask;
    }
}
