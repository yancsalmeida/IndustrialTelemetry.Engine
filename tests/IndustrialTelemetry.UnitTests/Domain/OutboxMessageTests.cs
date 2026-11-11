using IndustrialTelemetry.Domain.Entities;
using IndustrialTelemetry.Domain.Events;
using Shouldly;
using Xunit;

namespace IndustrialTelemetry.UnitTests.Domain;

public class OutboxMessageTests
{
    [Fact]
    public void Create_WithValidParameters_ShouldInitializeCorrectly()
    {
        var occurred = DateTime.UtcNow;
        var msg = OutboxMessage.Create("BatchStarted", "Batch", "BATCH-001", "{\"code\":\"BATCH-001\"}", occurred, "CORR-123");

        msg.Id.ShouldNotBe(Guid.Empty);
        msg.EventType.ShouldBe("BatchStarted");
        msg.AggregateType.ShouldBe("Batch");
        msg.AggregateId.ShouldBe("BATCH-001");
        msg.Payload.ShouldBe("{\"code\":\"BATCH-001\"}");
        msg.OccurredOnUtc.ShouldBe(occurred);
        msg.ProcessedOnUtc.ShouldBeNull();
        msg.RetryCount.ShouldBe(0);
        msg.LastError.ShouldBeNull();
        msg.CorrelationId.ShouldBe("CORR-123");
    }

    [Theory]
    [InlineData("", "Batch", "ID-1", "{}")]
    [InlineData("Event", "", "ID-1", "{}")]
    [InlineData("Event", "Batch", "", "{}")]
    [InlineData("Event", "Batch", "ID-1", "")]
    public void Create_WithEmptyArguments_ShouldThrowArgumentException(string eventType, string aggType, string aggId, string payload)
    {
        Should.Throw<ArgumentException>(() =>
            OutboxMessage.Create(eventType, aggType, aggId, payload, DateTime.UtcNow));
    }

    [Fact]
    public void FromDomainEvent_ShouldSerializeEventPayloadAutomatically()
    {
        var batchId = Guid.NewGuid();
        var occurred = DateTime.UtcNow;
        var evt = new BatchStartedDomainEvent(batchId, "B-101", "R-101", occurred, occurred);

        var msg = OutboxMessage.FromDomainEvent(evt, "Batch", batchId.ToString(), "CORR-456");

        msg.EventType.ShouldBe(nameof(BatchStartedDomainEvent));
        msg.AggregateType.ShouldBe("Batch");
        msg.AggregateId.ShouldBe(batchId.ToString());
        msg.Payload.ShouldContain("B-101");
        msg.Payload.ShouldContain(batchId.ToString());
        msg.CorrelationId.ShouldBe("CORR-456");
        msg.ProcessedOnUtc.ShouldBeNull();
    }

    [Fact]
    public void MarkAsProcessed_ShouldSetProcessedTimestampAndClearLastError()
    {
        var msg = OutboxMessage.Create("Event", "Agg", "1", "{}", DateTime.UtcNow);
        msg.RecordFailure("Network partition error");

        msg.RetryCount.ShouldBe(1);
        msg.LastError.ShouldBe("Network partition error");

        msg.MarkAsProcessed();

        msg.ProcessedOnUtc.ShouldNotBeNull();
        msg.ProcessedOnUtc.Value.ShouldBeInRange(DateTime.UtcNow.AddSeconds(-2), DateTime.UtcNow.AddSeconds(1));
        msg.LastError.ShouldBeNull();
    }

    [Fact]
    public void RecordFailure_ShouldIncrementRetryCountAndTruncateLongError()
    {
        var msg = OutboxMessage.Create("Event", "Agg", "1", "{}", DateTime.UtcNow);
        string longError = new string('E', 3000);

        msg.RecordFailure(longError);

        msg.RetryCount.ShouldBe(1);
        msg.LastError.ShouldNotBeNull();
        msg.LastError.Length.ShouldBe(2048);
        msg.ProcessedOnUtc.ShouldBeNull();

        msg.RecordFailure("Second failure");
        msg.RetryCount.ShouldBe(2);
        msg.LastError.ShouldBe("Second failure");
    }

    [Fact]
    public void ResetForRetry_ShouldClearProcessedTimestamp()
    {
        var msg = OutboxMessage.Create("Event", "Agg", "1", "{}", DateTime.UtcNow);
        msg.MarkAsProcessed();
        msg.ProcessedOnUtc.ShouldNotBeNull();

        msg.ResetForRetry();
        msg.ProcessedOnUtc.ShouldBeNull();
    }
}
