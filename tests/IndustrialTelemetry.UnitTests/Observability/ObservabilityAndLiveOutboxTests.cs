using System.Net;
using IndustrialTelemetry.Api.Controllers;
using IndustrialTelemetry.Application.DTOs;
using IndustrialTelemetry.Application.Interfaces;
using IndustrialTelemetry.Application.Services;
using IndustrialTelemetry.Domain.Entities;
using IndustrialTelemetry.Domain.Enums;
using IndustrialTelemetry.Infrastructure.Outbox;
using IndustrialTelemetry.Infrastructure.Persistence;
using IndustrialTelemetry.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace IndustrialTelemetry.UnitTests.Observability;

public class ObservabilityAndLiveOutboxTests
{
    [Fact]
    public async Task ObservabilityStream_ShouldBroadcastLiveEventsToSubscribers_AndCleanupOnCancel()
    {
        var stream = new ObservabilityStream();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var received = new List<ObservabilityEventRecord>();
        var subTask = Task.Run(async () =>
        {
            await foreach (var evt in stream.SubscribeAsync(cts.Token))
            {
                received.Add(evt);
                if (received.Count == 2)
                {
                    cts.Cancel();
                    break;
                }
            }
        });

        // Allow subscriber registration before publishing
        await Task.Delay(50);
        stream.GetStats().ActiveSubscribers.ShouldBe(1);

        stream.Publish("telemetry-batch", "Flushed 14 tags", new { count = 14 });
        stream.Publish("outbox-dispatched", "BatchCreated", new { id = 1 });

        await subTask;

        received.Count.ShouldBe(2);
        received[0].EventType.ShouldBe("telemetry-batch");
        received[1].EventType.ShouldBe("outbox-dispatched");

        await Task.Delay(30);
        stream.GetStats().ActiveSubscribers.ShouldBe(0);
        stream.GetStats().TotalEventsPublished.ShouldBe(2);
    }

    [Fact]
    public async Task LiveOutboxEventPublisher_ShouldSendWebhookAndBroadcastSse_AndThrowOnHttpError()
    {
        var stream = new ObservabilityStream();
        var capturedHeaders = new Dictionary<string, string>();
        var handler = new StubHttpHandler((req, _) =>
        {
            if (req.Headers.TryGetValues("X-Outbox-Event-Type", out var vals))
                capturedHeaders["EventType"] = vals.First();
            if (req.Headers.TryGetValues("X-Correlation-Id", out var corr))
                capturedHeaders["CorrelationId"] = corr.First();
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });

        using var publisher = new LiveOutboxEventPublisher(
            stream,
            Options.Create(new OutboxOptions { WebhookUrls = ["https://hooks.local/events"] }),
            handler: handler);

        var msg = OutboxMessage.Create(
            "BatchCreatedDomainEvent",
            "Batch",
            Guid.NewGuid().ToString(),
            "{\"batchCode\":\"BATCH-001\"}",
            DateTime.UtcNow,
            correlationId: "corr-42");

        await publisher.PublishAsync(msg);

        publisher.TotalDispatchedCount.ShouldBe(1);
        publisher.WebhookSuccessCount.ShouldBe(1);
        publisher.WebhookFailureCount.ShouldBe(0);
        capturedHeaders["EventType"].ShouldBe("BatchCreatedDomainEvent");
        capturedHeaders["CorrelationId"].ShouldBe("corr-42");
        stream.GetStats().RecentEvents.Last().EventType.ShouldBe("outbox-dispatched");

        // Failure case must throw so OutboxProcessor records retry
        using var failingHandler = new StubHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var failingPublisher = new LiveOutboxEventPublisher(
            stream,
            Options.Create(new OutboxOptions { WebhookUrls = ["https://hooks.local/down"] }),
            handler: failingHandler);

        await Should.ThrowAsync<HttpRequestException>(() => failingPublisher.PublishAsync(msg));
        failingPublisher.WebhookFailureCount.ShouldBe(1);
    }

    [Fact]
    public async Task ObservabilityController_RelationalSqlite_ShouldQueryHistoryRateOfChangeAlertsAndRequeueOutbox()
    {
        await using var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();

        var options = new DbContextOptionsBuilder<IndustrialDbContext>()
            .UseSqlite(conn)
            .Options;

        await using var db = new IndustrialDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var batch = Batch.Create("BATCH-OBS-01", "CSTR-SP", "R-201", 100.0);
        batch.Start(DateTime.UtcNow.AddMinutes(-5));
        await db.Batches.AddAsync(batch);

        var t0 = DateTime.UtcNow.AddSeconds(-30);
        var m1 = TelemetryMeasurement.Create(batch.Id, t0, "SP-TT-201", 65.0, MeasurementUnit.Celsius, TelemetryQuality.Good);
        var m2 = TelemetryMeasurement.Create(batch.Id, t0.AddSeconds(30), "SP-TT-201", 75.0, MeasurementUnit.Celsius, TelemetryQuality.Good);
        var m3 = TelemetryMeasurement.Create(batch.Id, t0.AddSeconds(30), "SP-AT-201-CB", 0.34, MeasurementUnit.MolPerLiter, TelemetryQuality.Good);
        await db.Telemetry.AddRangeAsync(m1, m2, m3);

        var alert = ProcessAlert.Create(batch.Id, "SP-TT-201", DateTime.UtcNow, 130.0, 126.85, AlertSeverity.Critical, "High temp excursion");
        await db.ProcessAlerts.AddAsync(alert);

        var processedOutbox = OutboxMessage.Create("BatchCreatedDomainEvent", "Batch", batch.Id.ToString(), "{}", DateTime.UtcNow.AddMinutes(-2));
        processedOutbox.MarkAsProcessed();

        var deadOutbox = OutboxMessage.Create("CriticalAlertTriggered", "ProcessAlert", alert.Id.ToString(), "{\"tag\":\"SP-TT-201\"}", DateTime.UtcNow.AddMinutes(-1));
        for (int i = 0; i < 5; i++) deadOutbox.RecordFailure("Webhook 503");

        await db.OutboxMessages.AddRangeAsync(processedOutbox, deadOutbox);
        await db.SaveChangesAsync();

        var telemetryRepo = new TelemetryRepository(db);
        var alertRepo = new AlertRepository(db);
        var outboxRepo = new OutboxRepository(db);
        var unitOfWork = new UnitOfWork(db);
        var channel = Substitute.For<ITelemetryChannel>();
        channel.GetStatus().Returns(new ChannelStatusDto(0, 50000, 3, 3, 0, false));
        var stream = new ObservabilityStream();
        stream.Publish("outbox-dispatched", "ok", new { });
        using var publisher = new LiveOutboxEventPublisher(stream);

        var controller = new ObservabilityController(
            telemetryRepo, alertRepo, outboxRepo, unitOfWork, channel, stream, publisher,
            Options.Create(new OutboxOptions { MaxRetries = 5 }));

        var overview = ((OkObjectResult)await controller.GetOverview()).Value.ShouldBeOfType<ObservabilityOverviewDto>();
        overview.TotalMeasurements.ShouldBe(3);
        overview.ActiveSensorTagsCount.ShouldBe(2);
        overview.TotalAlerts.ShouldBe(1);
        overview.UnacknowledgedAlerts.ShouldBe(1);
        overview.CriticalAlerts.ShouldBe(1);
        overview.OutboxProcessedCount.ShouldBe(1);
        overview.OutboxDeadLetterCount.ShouldBe(1);
        overview.OutboxPendingCount.ShouldBe(3); // BatchCreated, BatchStarted, ProcessAlertTriggered

        var sensors = ((OkObjectResult)await controller.GetSensors()).Value.ShouldBeOfType<List<SensorSummaryDto>>();
        var tt201 = sensors.Single(s => s.SensorTag == "SP-TT-201");
        tt201.LatestValue.ShouldBe(75.0);
        tt201.RateOfChangePerMin.ShouldBe(20.0, 0.2);

        var ts = ((OkObjectResult)await controller.GetTimeSeries("sp-tt-201")).Value.ShouldBeOfType<List<TimeSeriesPointDto>>();
        ts.Count.ShouldBe(2);
        ts[0].Value.ShouldBe(65.0);
        ts[1].Value.ShouldBe(75.0);

        var alerts = ((OkObjectResult)await controller.GetRecentAlerts(unacknowledgedOnly: true)).Value.ShouldBeOfType<List<ProcessAlertResponseDto>>();
        alerts.Count.ShouldBe(1);

        var outboxAudit = ((OkObjectResult)await controller.GetOutboxAudit(deadLetterOnly: true)).Value.ShouldBeOfType<OutboxSummaryDto>();
        outboxAudit.RecentMessages.Count.ShouldBe(1);
        outboxAudit.RecentMessages[0].Status.ShouldBe("DEAD_LETTER");

        var requeued = ((OkObjectResult)await controller.RequeueFailedOutboxMessages()).Value.ShouldBeOfType<RequeueDeadLettersResultDto>();
        requeued.RequeuedCount.ShouldBe(1);

        var afterRequeue = ((OkObjectResult)await controller.GetOutboxAudit(pendingOnly: true)).Value.ShouldBeOfType<OutboxSummaryDto>();
        afterRequeue.PendingCount.ShouldBe(4);
        afterRequeue.DeadLetterCount.ShouldBe(0);
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request, cancellationToken));
    }
}
