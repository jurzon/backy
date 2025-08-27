using Commitments.Api.Payments;
using Commitments.Domain.Entities;
using Commitments.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Commitments.Tests.Payments;

public class PaymentRetryWorkerTests
{
    private AppDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(opts);
    }

    private PaymentIntentLog FailedLog(Guid commitmentId, int attempt, string? errorCode = null)
        => new()
        {
            CommitmentId = commitmentId,
            StripePaymentIntentId = $"pi_{Guid.NewGuid():N}",
            AmountMinor = 1000,
            Currency = "eur",
            Status = "failed",
            AttemptNumber = attempt,
            LastErrorCode = errorCode,
            UpdatedAtUtc = DateTime.UtcNow.AddDays(-2) // old enough for retry
        };

    [Fact]
    public async Task Retries_failed_non_hard_decline_and_marks_succeeded()
    {
        using var db = CreateDb();
        var commitmentId = Guid.NewGuid();
        db.PaymentIntentLogs.Add(FailedLog(commitmentId, 1));
        await db.SaveChangesAsync();
        var worker = new PaymentRetryWorker(db, new DummyPaymentService(), NullLogger<PaymentRetryWorker>.Instance);

        await worker.RunAsync();

        var log = db.PaymentIntentLogs.Single();
        log.Status.Should().Be("succeeded");
        log.AttemptNumber.Should().Be(2);
    }

    [Fact]
    public async Task Skips_hard_decline()
    {
        using var db = CreateDb();
        var commitmentId = Guid.NewGuid();
        db.PaymentIntentLogs.Add(FailedLog(commitmentId, 1, "card_declined"));
        await db.SaveChangesAsync();
        var worker = new PaymentRetryWorker(db, new DummyPaymentService(), NullLogger<PaymentRetryWorker>.Instance);

        await worker.RunAsync();

        var log = db.PaymentIntentLogs.Single();
        log.Status.Should().Be("failed");
        log.AttemptNumber.Should().Be(1);
    }

    private class DummyPaymentService : IPaymentService
    {
        public Task<PaymentIntentLog> CreateFailurePaymentIntentAsync(Commitment commitment, CancellationToken ct = default) => throw new NotImplementedException();
        public Task EnsureSetupIntentAsync(Guid userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdatePaymentStatusAsync(string paymentIntentId, string newStatus, string? errorCode = null, CancellationToken ct = default) => Task.CompletedTask;
    }
}
