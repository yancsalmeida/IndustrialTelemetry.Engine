using IndustrialTelemetry.Application.Interfaces;
using IndustrialTelemetry.Domain.Entities;
using IndustrialTelemetry.Infrastructure.BackgroundServices;
using IndustrialTelemetry.Infrastructure.Outbox;
using IndustrialTelemetry.Infrastructure.Persistence;
using IndustrialTelemetry.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace IndustrialTelemetry.UnitTests.Infrastructure;

public class OutboxProcessorBackgroundServiceTests
{
    private static (IServiceScopeFactory ScopeFactory, IndustrialDbContext Context, InMemoryEventPublisher Publisher) CreateTestEnvironment(
        OutboxOptions? options = null)
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString();

        services.AddDbContext<IndustrialDbContext>(opt => opt.UseInMemoryDatabase(dbName));
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        var publisher = new InMemoryEventPublisher();
        services.AddSingleton<IEventPublisher>(publisher);

        if (options != null)
        {
            services.AddSingleton(Options.Create(options));
        }

        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var context = provider.GetRequiredService<IndustrialDbContext>();

        return (scopeFactory, context, publisher);
    }

    [Fact]
    public async Task ProcessPendingBatchAsync_WhenMessagesExist_ShouldPublishAndMarkAsProcessed()
    {
        var (scopeFactory, context, publisher) = CreateTestEnvironment();

        var msg1 = OutboxMessage.Create("BatchCreated", "Batch", "1", "{}", DateTime.UtcNow.AddSeconds(-2));
        var msg2 = OutboxMessage.Create("BatchStarted", "Batch", "1", "{}", DateTime.UtcNow.AddSeconds(-1));

        await context.OutboxMessages.AddRangeAsync(msg1, msg2);
        await context.SaveChangesAsync();

        var service = new OutboxProcessorBackgroundService(
            scopeFactory,
            NullLogger<OutboxProcessorBackgroundService>.Instance,
            Options.Create(new OutboxOptions { BatchSize = 10, PollIntervalMs = 50, MaxRetries = 3 }));

        // Act
        int processed = await service.ProcessPendingBatchAsync();

        // Assert
        processed.ShouldBe(2);
        publisher.PublishedMessages.Count.ShouldBe(2);

        var updated = await context.OutboxMessages.AsNoTracking().ToListAsync();
        updated.ShouldAllBe(m => m.ProcessedOnUtc != null);
        updated.ShouldAllBe(m => m.RetryCount == 0);
        updated.ShouldAllBe(m => m.LastError == null);
    }

    [Fact]
    public async Task ProcessPendingBatchAsync_WhenBrokerTransientErrorOccurs_ShouldRecordFailureAndRetryLater()
    {
        var (scopeFactory, context, publisher) = CreateTestEnvironment();

        var msg1 = OutboxMessage.Create("BatchCreated", "Batch", "1", "{}", DateTime.UtcNow.AddSeconds(-2));
        var msg2 = OutboxMessage.Create("BatchStarted", "Batch", "1", "{}", DateTime.UtcNow.AddSeconds(-1));

        await context.OutboxMessages.AddRangeAsync(msg1, msg2);
        await context.SaveChangesAsync();

        // Simulate 1 network failure on the first message
        publisher.FailNextPublishCount = 1;

        var service = new OutboxProcessorBackgroundService(
            scopeFactory,
            NullLogger<OutboxProcessorBackgroundService>.Instance,
            Options.Create(new OutboxOptions { BatchSize = 10, PollIntervalMs = 50, MaxRetries = 3 }));

        // Act - First run
        int firstRunCount = await service.ProcessPendingBatchAsync();
        firstRunCount.ShouldBe(1); // 1 succeeded, 1 failed

        var dbMessages = await context.OutboxMessages.AsNoTracking().OrderBy(m => m.OccurredOnUtc).ToListAsync();
        dbMessages[0].ProcessedOnUtc.ShouldBeNull();
        dbMessages[0].RetryCount.ShouldBe(1);
        dbMessages[0].LastError.ShouldNotBeNull();
        dbMessages[0].LastError!.ShouldContain("Broker network failure");

        dbMessages[1].ProcessedOnUtc.ShouldNotBeNull();
        dbMessages[1].RetryCount.ShouldBe(0);

        // Act - Second run: broker is now recovered
        int secondRunCount = await service.ProcessPendingBatchAsync();
        secondRunCount.ShouldBe(1);

        var finalDbMessages = await context.OutboxMessages.AsNoTracking().OrderBy(m => m.OccurredOnUtc).ToListAsync();
        finalDbMessages[0].ProcessedOnUtc.ShouldNotBeNull();
        finalDbMessages[0].LastError.ShouldBeNull(); // Cleared on success
        publisher.PublishedMessages.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ProcessPendingBatchAsync_WhenRetryCountReachesMaxRetries_ShouldSkipMessageAsDeadLetter()
    {
        var (scopeFactory, context, publisher) = CreateTestEnvironment();

        var poisonMessage = OutboxMessage.Create("CorruptEvent", "Batch", "666", "{}", DateTime.UtcNow);
        // Exceeded max retries (3)
        for (int i = 0; i < 3; i++)
        {
            poisonMessage.RecordFailure("Unprocessable entity");
        }

        await context.OutboxMessages.AddAsync(poisonMessage);
        await context.SaveChangesAsync();

        var service = new OutboxProcessorBackgroundService(
            scopeFactory,
            NullLogger<OutboxProcessorBackgroundService>.Instance,
            Options.Create(new OutboxOptions { BatchSize = 10, PollIntervalMs = 50, MaxRetries = 3 }));

        // Act
        int processed = await service.ProcessPendingBatchAsync();

        // Assert
        processed.ShouldBe(0);
        publisher.PublishedMessages.ShouldBeEmpty();

        var reloaded = await context.OutboxMessages.AsNoTracking().FirstAsync();
        reloaded.ProcessedOnUtc.ShouldBeNull();
        reloaded.RetryCount.ShouldBe(3);
    }
}
