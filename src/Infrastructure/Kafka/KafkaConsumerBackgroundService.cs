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
        var bootstrapServers = _configuration["Kafka:BootstrapServers"];
        if (string.IsNullOrWhiteSpace(bootstrapServers) || bootstrapServers.Equals("disabled", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Kafka:BootstrapServers is not configured or disabled. Kafka consumer will not start.");
            return;
        }

        var groupId = _configuration["Kafka:GroupId"] ?? "multivendor-order-fulfillment-group";

        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false, // Manual offset commit after successful handling or DLQ
            EnablePartitionEof = false
        };

        ApplyCloudSecuritySettings(config, _configuration, bootstrapServers);

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
                try { Task.Delay(2000, stoppingToken).Wait(stoppingToken); } catch { }
            }
        }

        consumer.Close();
        _logger.LogInformation("Kafka consumer shut down gracefully.");
    }

    private static void ApplyCloudSecuritySettings(ClientConfig config, IConfiguration configuration, string bootstrapServers)
    {
        var securityProtocolStr = configuration["Kafka:SecurityProtocol"];
        if (Enum.TryParse<SecurityProtocol>(securityProtocolStr, true, out var secProto))
        {
            config.SecurityProtocol = secProto;
        }
        else if (bootstrapServers.Contains("aivencloud.com") || bootstrapServers.Contains("confluent.cloud"))
        {
            var hasSasl = !string.IsNullOrWhiteSpace(configuration["Kafka:SaslUsername"]);
            config.SecurityProtocol = hasSasl ? SecurityProtocol.SaslSsl : SecurityProtocol.Ssl;
        }

        var saslMechStr = configuration["Kafka:SaslMechanism"];
        if (Enum.TryParse<SaslMechanism>(saslMechStr, true, out var saslMech))
        {
            config.SaslMechanism = saslMech;
        }
        else if (config.SecurityProtocol is SecurityProtocol.SaslPlaintext or SecurityProtocol.SaslSsl)
        {
            config.SaslMechanism = SaslMechanism.ScramSha256;
        }

        var saslUsername = configuration["Kafka:SaslUsername"];
        if (!string.IsNullOrWhiteSpace(saslUsername))
        {
            config.SaslUsername = saslUsername;
        }

        var saslPassword = configuration["Kafka:SaslPassword"];
        if (!string.IsNullOrWhiteSpace(saslPassword))
        {
            config.SaslPassword = saslPassword;
        }
    }
}

public interface IKafkaEventDispatcher
{
    Task DispatchAsync(string topic, string key, string payloadJson, CancellationToken ct = default);
}
