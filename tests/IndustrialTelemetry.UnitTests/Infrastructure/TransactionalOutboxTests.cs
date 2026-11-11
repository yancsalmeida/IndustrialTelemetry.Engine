using IndustrialTelemetry.Application.Services;
using IndustrialTelemetry.Domain.Entities;
using IndustrialTelemetry.Domain.Enums;
using IndustrialTelemetry.Domain.Events;
using IndustrialTelemetry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace IndustrialTelemetry.UnitTests.Infrastructure;

public class TransactionalOutboxTests
{
    private static IndustrialDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<IndustrialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new IndustrialDbContext(options);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenBatchCreated_ShouldPersistOutboxMessageAtomically()
    {
        using var context = CreateInMemoryContext();

        var batch = Batch.Create("BATCH-TX-001", "Polymer-X", "R-101", 20.0, "Testing outbox");
        batch.DomainEvents.Count.ShouldBe(1);

        await context.Batches.AddAsync(batch);
        await context.SaveChangesAsync();

        // Entity events must be cleared after commit
        batch.DomainEvents.ShouldBeEmpty();

        // Outbox record must be present in database
        var outboxMessages = await context.OutboxMessages.ToListAsync();
        outboxMessages.Count.ShouldBe(1);

        var msg = outboxMessages[0];
        msg.EventType.ShouldBe(nameof(BatchCreatedDomainEvent));
        msg.AggregateType.ShouldBe(nameof(Batch));
        msg.AggregateId.ShouldBe(batch.Id.ToString());
        msg.ProcessedOnUtc.ShouldBeNull();
        msg.RetryCount.ShouldBe(0);
        msg.Payload.ShouldContain("BATCH-TX-001");
        msg.Payload.ShouldContain("Polymer-X");
    }

    [Fact]
    public async Task SaveChangesAsync_WhenBatchStateTransitions_ShouldAccumulateSequentialOutboxMessages()
    {
        using var context = CreateInMemoryContext();

        var batch = Batch.Create("BATCH-TX-002", "Resin-B", "R-201", 15.0);
        await context.Batches.AddAsync(batch);
        await context.SaveChangesAsync();

        // 1. Start batch
        batch.Start(DateTime.UtcNow);
        await context.SaveChangesAsync();

        // 2. Complete batch
        batch.Complete(DateTime.UtcNow.AddHours(2));
        await context.SaveChangesAsync();

        var outboxMessages = await context.OutboxMessages
            .OrderBy(x => x.OccurredOnUtc)
            .ToListAsync();

        outboxMessages.Count.ShouldBe(3);
        outboxMessages[0].EventType.ShouldBe(nameof(BatchCreatedDomainEvent));
        outboxMessages[1].EventType.ShouldBe(nameof(BatchStartedDomainEvent));
        outboxMessages[2].EventType.ShouldBe(nameof(BatchCompletedDomainEvent));
    }

    [Fact]
    public async Task SaveChangesAsync_WhenBatchAborted_ShouldPersistBatchAbortedOutboxMessage()
    {
        using var context = CreateInMemoryContext();

        var batch = Batch.Create("BATCH-TX-003", "Solvent-C", "R-301", 5.0);
        await context.Batches.AddAsync(batch);
        await context.SaveChangesAsync();

        batch.Start(DateTime.UtcNow);
        batch.Abort("Exothermic runaway hazard detected", DateTime.UtcNow.AddMinutes(5));
        await context.SaveChangesAsync();

        var abortedMsg = await context.OutboxMessages
            .FirstOrDefaultAsync(x => x.EventType == nameof(BatchAbortedDomainEvent));

        abortedMsg.ShouldNotBeNull();
        abortedMsg.Payload.ShouldContain("Exothermic runaway hazard detected");
    }

    [Fact]
    public async Task SaveChangesAsync_WhenProcessAlertTriggeredAndAcknowledged_ShouldEmitOutboxEvents()
    {
        using var context = CreateInMemoryContext();
        var batchId = Guid.NewGuid();

        var alert = ProcessAlert.Create(
            batchId, "TT-101", DateTime.UtcNow, 95.5, 90.0, AlertSeverity.Critical, "Temperature high");

        await context.ProcessAlerts.AddAsync(alert);
        await context.SaveChangesAsync();

        var alertMsg = await context.OutboxMessages
            .FirstOrDefaultAsync(x => x.EventType == nameof(ProcessAlertTriggeredDomainEvent));

        alertMsg.ShouldNotBeNull();
        alertMsg.AggregateType.ShouldBe(nameof(ProcessAlert));
        alertMsg.AggregateId.ShouldBe(alert.Id.ToString());
        alertMsg.Payload.ShouldContain("TT-101");
        alertMsg.Payload.ShouldContain("95.5");

        // Acknowledge alert
        alert.Acknowledge("Operator-42", DateTime.UtcNow);
        await context.SaveChangesAsync();

        var ackMsg = await context.OutboxMessages
            .FirstOrDefaultAsync(x => x.EventType == nameof(ProcessAlertAcknowledgedDomainEvent));

        ackMsg.ShouldNotBeNull();
        ackMsg.Payload.ShouldContain("Operator-42");
    }

    [Fact]
    public async Task SaveChangesAsync_WhenRateOfChangeAnomalyTriggered_ShouldEmitProcessAlertTriggeredOutboxEvent()
    {
        using var context = CreateInMemoryContext();
        var batchId = Guid.NewGuid();
        var detector = new SlidingWindowAnomalyDetector(alertCooldown: TimeSpan.Zero);
        var t0 = new DateTime(2026, 11, 11, 14, 0, 0, DateTimeKind.Utc);

        var rule = AlertRule.CreateRateOfChange(
            "TT-101",
            maxRateOfChangePerMinute: 5.0,
            minRateOfChangePerMinute: null,
            AlertSeverity.Critical,
            "CRITICAL: Reactor thermal runaway rapid rate-of-temperature-rise excursion (dT/dt > 5.0 °C/min)",
            windowSeconds: 30
        );

        // Baseline measurement at t0 (85.0 °C)
        var m1 = TelemetryMeasurement.Create(batchId, t0, "TT-101", 85.0, MeasurementUnit.Celsius);
        detector.Evaluate(m1, new[] { rule });

        // Rapid temperature excursion 30s later to 88.0 °C (dT/dt = +6.00 °C/min > 5.0 °C/min)
        var m2 = TelemetryMeasurement.Create(batchId, t0.AddSeconds(30), "TT-101", 88.0, MeasurementUnit.Celsius);
        var alerts = detector.Evaluate(m2, new[] { rule });

        alerts.Count.ShouldBe(1);
        var alert = alerts[0];

        // Save triggered alert to EF Core Context
        await context.ProcessAlerts.AddAsync(alert);
        await context.SaveChangesAsync();

        // Verify domain event was converted to PostgreSQL Outbox record
        var outboxMessage = await context.OutboxMessages
            .FirstOrDefaultAsync(x => x.EventType == nameof(ProcessAlertTriggeredDomainEvent) && x.AggregateId == alert.Id.ToString());

        outboxMessage.ShouldNotBeNull();
        outboxMessage.AggregateType.ShouldBe(nameof(ProcessAlert));
        outboxMessage.Payload.ShouldContain("RAPID RATE-OF-CHANGE DETECTED on TT-101");
        outboxMessage.Payload.ShouldContain("6.00 Celsius/min");
        outboxMessage.Payload.ShouldContain("Critical");
    }
}
