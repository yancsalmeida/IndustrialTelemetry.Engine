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

    [Fact]
    public void HistorianTelemetry_ShouldExposeChannelAndOutboxMetricsViaMeterListener()
    {
        var channel = Substitute.For<ITelemetryChannel>();
        channel.GetStatus().Returns(new ChannelStatusDto(
            CurrentCount: 12,
            Capacity: 50000,
            TotalIngested: 400,
            TotalDrained: 388,
            TotalDropped: 0,
            IsWriterCompleted: false));

        var stream = new ObservabilityStream();
        var dispatchMetrics = Substitute.For<IOutboxDispatchMetrics>();
        dispatchMetrics.TotalDispatchedCount.Returns(9);

        using var telemetry = new IndustrialTelemetry.Api.Observability.HistorianTelemetry(
            channel, stream, dispatchMetrics);

        var recorded = new Dictionary<string, double>();
        using var listener = new System.Diagnostics.Metrics.MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == IndustrialTelemetry.Api.Observability.HistorianTelemetry.ServiceName)
                    meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<int>((inst, val, _, _) => recorded[inst.Name] = val);
        listener.SetMeasurementEventCallback<long>((inst, val, _, _) => recorded[inst.Name] = val);
        listener.Start();
        listener.RecordObservableInstruments();

        recorded["historian_channel_depth"].ShouldBe(12);
        recorded["historian_channel_capacity"].ShouldBe(50000);
        recorded["historian_channel_ingested_total"].ShouldBe(400);
        recorded["historian_channel_drained_total"].ShouldBe(388);
        recorded["historian_channel_dropped_total"].ShouldBe(0);
        recorded["historian_outbox_dispatched_total"].ShouldBe(9);
        recorded["historian_sse_subscribers_active"].ShouldBe(0);
    }

    [Fact]
    public void SpcAnalyticsService_ShouldComputeEwmaLimitsAndGoldenBatchMetrics()
    {
        var batchGold = Guid.NewGuid();
        var batchCand = Guid.NewGuid();
        var t0 = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

        var spcSamples = new List<TelemetryMeasurement>();
        for (int i = 0; i < 20; i++)
        {
            double val = i < 17 ? 62.0 + (i % 2) * 0.2 : 95.0; // sudden step shift
            spcSamples.Add(TelemetryMeasurement.Create(batchGold, t0.AddSeconds(i), "SP-TT-201", val, MeasurementUnit.Celsius, TelemetryQuality.Good));
        }

        var spc = SpcAnalyticsService.ComputeSpcChart("SP-TT-201", batchGold, spcSamples, lambda: 0.3, sigmaMultiplier: 2.0);
        spc.SampleCount.ShouldBe(20);
        spc.EwmaBreachCount.ShouldBeGreaterThan(0);
        spc.ShewhartBreachCount.ShouldBeGreaterThan(0);
        spc.MaxZScore.ShouldBeGreaterThan(2.0);
        spc.ShewhartUcl.ShouldBeGreaterThan(spc.Mean);

        var golden = new[]
        {
            TelemetryMeasurement.Create(batchGold, t0, "SP-TT-201", 60.0, MeasurementUnit.Celsius, TelemetryQuality.Good),
            TelemetryMeasurement.Create(batchGold, t0.AddSeconds(10), "SP-TT-201", 70.0, MeasurementUnit.Celsius, TelemetryQuality.Good),
            TelemetryMeasurement.Create(batchGold, t0.AddSeconds(20), "SP-TT-201", 80.0, MeasurementUnit.Celsius, TelemetryQuality.Good),
        };
        var candidate = new[]
        {
            TelemetryMeasurement.Create(batchCand, t0.AddHours(1), "SP-TT-201", 62.0, MeasurementUnit.Celsius, TelemetryQuality.Good),
            TelemetryMeasurement.Create(batchCand, t0.AddHours(1).AddSeconds(20), "SP-TT-201", 82.0, MeasurementUnit.Celsius, TelemetryQuality.Good),
        };

        var cmp = SpcAnalyticsService.ComputeGoldenBatchComparison("SP-TT-201", batchGold, batchCand, golden, candidate);
        cmp.AlignedPointCount.ShouldBe(3);
        cmp.MeanBias.ShouldBe(2.0, 0.01);
        cmp.Rmse.ShouldBe(2.0, 0.01);
        cmp.Mae.ShouldBe(2.0, 0.01);
    }

    [Fact]
    public async Task ObservabilityController_ShouldServeDownsamplingSpcAndGoldenBatchEndpoints()
    {
        await using var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();
        var options = new DbContextOptionsBuilder<IndustrialDbContext>().UseSqlite(conn).Options;
        await using var db = new IndustrialDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var b1 = Batch.Create("BATCH-GOLD", "CSTR-SP", "R-201", 100.0);
        var b2 = Batch.Create("BATCH-CAND", "CSTR-SP", "R-201", 100.0);
        await db.Batches.AddRangeAsync(b1, b2);

        var baseTime = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
        await db.Telemetry.AddRangeAsync(
            TelemetryMeasurement.Create(b1.Id, baseTime.AddSeconds(0), "SP-TT-201", 60.0, MeasurementUnit.Celsius, TelemetryQuality.Good),
            TelemetryMeasurement.Create(b1.Id, baseTime.AddSeconds(2), "SP-TT-201", 64.0, MeasurementUnit.Celsius, TelemetryQuality.Good),
            TelemetryMeasurement.Create(b1.Id, baseTime.AddSeconds(6), "SP-TT-201", 68.0, MeasurementUnit.Celsius, TelemetryQuality.Good),
            TelemetryMeasurement.Create(b2.Id, baseTime.AddMinutes(5).AddSeconds(0), "SP-TT-201", 61.0, MeasurementUnit.Celsius, TelemetryQuality.Good),
            TelemetryMeasurement.Create(b2.Id, baseTime.AddMinutes(5).AddSeconds(6), "SP-TT-201", 71.0, MeasurementUnit.Celsius, TelemetryQuality.Good)
        );
        await db.SaveChangesAsync();

        var telemetryRepo = new TelemetryRepository(db);
        var controller = new ObservabilityController(
            telemetryRepo,
            new AlertRepository(db),
            new OutboxRepository(db),
            new UnitOfWork(db),
            Substitute.For<ITelemetryChannel>(),
            new ObservabilityStream(),
            spcAnalytics: new SpcAnalyticsService(telemetryRepo));

        var buckets = ((OkObjectResult)await controller.GetDownsampledSeries("SP-TT-201", bucketSeconds: 5, batchId: b1.Id))
            .Value.ShouldBeOfType<List<DownsampledBucketDto>>();
        buckets.Count.ShouldBe(2);
        buckets[0].SampleCount.ShouldBe(2);
        buckets[0].AvgValue.ShouldBe(62.0);
        buckets[0].MinValue.ShouldBe(60.0);
        buckets[0].MaxValue.ShouldBe(64.0);

        var spc = ((OkObjectResult)await controller.GetSpcAnalysis("SP-TT-201", batchId: b1.Id))
            .Value.ShouldBeOfType<SpcControlChartDto>();
        spc.SampleCount.ShouldBe(3);

        var goldenCmp = ((OkObjectResult)await controller.CompareGoldenBatch("SP-TT-201", b1.Id, b2.Id))
            .Value.ShouldBeOfType<GoldenBatchComparisonDto>();
        goldenCmp.AlignedPointCount.ShouldBe(3);
        goldenCmp.MaxAbsoluteDeviation.ShouldBeGreaterThan(0.5);
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request, cancellationToken));
    }
}
