using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MultiVendorPlatform.Infrastructure.Kafka;

public class KafkaConsumerBackgroundService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRetryAndDeadLetterHandler _retryHandler;
    private readonly ILogger<KafkaConsumerBackgroundService> _logger;

    public KafkaConsumerBackgroundService(
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        IRetryAndDeadLetterHandler retryHandler,
        ILogger<KafkaConsumerBackgroundService> logger)
    {
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _retryHandler = retryHandler;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run Kafka consumer loop on dedicated background thread
        Task.Run(() => StartConsumerLoop(stoppingToken), stoppingToken);
        return Task.CompletedTask;
    }

    private void StartConsumerLoop(CancellationToken stoppingToken)
    {
        var bootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        var groupId = _configuration["Kafka:GroupId"] ?? "multivendor-order-fulfillment-group";

        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false, // Manual offset commit after successful handling or DLQ
            EnablePartitionEof = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();

        var topics = new[]
        {
            KafkaTopics.OrderCreated,
            KafkaTopics.PaymentCompleted,
            KafkaTopics.PaymentFailed,
            KafkaTopics.ShipmentCreated,
            KafkaTopics.OrderDelivered
        };

        consumer.Subscribe(topics);
        _logger.LogInformation("Kafka consumer subscribed to topics: {Topics}", string.Join(", ", topics));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var consumeResult = consumer.Consume(TimeSpan.FromSeconds(1));
                if (consumeResult == null || consumeResult.IsPartitionEOF)
                {
                    continue;
                }

                var topic = consumeResult.Topic;
                var key = consumeResult.Message.Key ?? string.Empty;
                var payloadJson = consumeResult.Message.Value;

                _logger.LogInformation("Kafka consumed message from {Topic} [Partition: {Partition}, Offset: {Offset}, Key: {Key}]",
                    topic, consumeResult.Partition.Value, consumeResult.Offset.Value, key);

                // Process message inside retry & DLQ wrapper
                _retryHandler.ExecuteWithRetryAndDlqAsync(
                    topic,
                    key,
                    payloadJson,
                    async () =>
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var dispatcher = scope.ServiceProvider.GetRequiredService<IKafkaEventDispatcher>();
                        await dispatcher.DispatchAsync(topic, key, payloadJson, stoppingToken);
                    },
                    stoppingToken).GetAwaiter().GetResult();

                // Commit offset manually
                consumer.Commit(consumeResult);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in Kafka consumer loop.");
            }
        }

        consumer.Close();
        _logger.LogInformation("Kafka consumer shut down gracefully.");
    }
}

public interface IKafkaEventDispatcher
{
    Task DispatchAsync(string topic, string key, string payloadJson, CancellationToken ct = default);
}
