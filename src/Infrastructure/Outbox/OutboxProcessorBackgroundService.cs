using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MultiVendorPlatform.Infrastructure.Kafka;

namespace MultiVendorPlatform.Infrastructure.Outbox;

public class OutboxProcessorBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IKafkaProducer _kafkaProducer;
    private readonly ILogger<OutboxProcessorBackgroundService> _logger;
    private const int BatchSize = 50;
    private const int MaxRetries = 5;

    public OutboxProcessorBackgroundService(
        IServiceScopeFactory scopeFactory,
        IKafkaProducer kafkaProducer,
        ILogger<OutboxProcessorBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _kafkaProducer = kafkaProducer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox Processor Background Service started.");
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessOutboxBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error occurred during Outbox processing cycle.");
            }
        }

        _logger.LogInformation("Outbox Processor Background Service stopped.");
    }

    private async Task ProcessOutboxBatchAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var outboxService = scope.ServiceProvider.GetRequiredService<IOutboxService>();

        var pendingMessages = await outboxService.GetPendingMessagesAsync(BatchSize, stoppingToken);
        if (!pendingMessages.Any()) return;

        _logger.LogInformation("Processing {Count} pending outbox messages.", pendingMessages.Count);

        foreach (var message in pendingMessages)
        {
            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                await _kafkaProducer.PublishRawAsync(
                    message.Topic,
                    message.AggregateId,
                    message.PayloadJson,
                    null,
                    stoppingToken);

                await outboxService.MarkAsPublishedAsync(message.Id, stoppingToken);
                _logger.LogDebug("Successfully published outbox message {MessageId} of type {EventType}",
                    message.Id, message.EventType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish outbox message {MessageId} to {Topic}. Attempt {RetryCount}/{MaxRetries}",
                    message.Id, message.Topic, message.RetryCount + 1, MaxRetries);

                var isDeadLetter = message.RetryCount + 1 >= MaxRetries;
                await outboxService.MarkAsFailedAsync(message.Id, ex.Message, isDeadLetter, stoppingToken);
            }
        }
    }
}
