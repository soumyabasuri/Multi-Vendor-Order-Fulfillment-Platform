using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MultiVendorPlatform.Infrastructure.Kafka;

public interface IKafkaProducer
{
    Task PublishAsync<T>(string topic, string key, T message, string? correlationId = null, CancellationToken ct = default);
    Task PublishRawAsync(string topic, string key, string payloadJson, string? correlationId = null, CancellationToken ct = default);
}

public class KafkaProducer : IKafkaProducer, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaProducer> _logger;

    public KafkaProducer(IConfiguration configuration, ILogger<KafkaProducer> logger)
    {
        _logger = logger;
        var bootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092";

        var config = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.All, // Strong durability guarantee
            EnableIdempotence = true, // Prevent duplicate writes at producer level
            MessageSendMaxRetries = 3,
            RetryBackoffMs = 100,
            LingerMs = 5
        };

        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task PublishAsync<T>(string topic, string key, T message, string? correlationId = null, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(message, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await PublishRawAsync(topic, key, json, correlationId, ct);
    }

    public async Task PublishRawAsync(string topic, string key, string payloadJson, string? correlationId = null, CancellationToken ct = default)
    {
        try
        {
            var kafkaMessage = new Message<string, string>
            {
                Key = key,
                Value = payloadJson,
                Timestamp = new Timestamp(DateTime.UtcNow),
                Headers = new Headers()
            };

            if (!string.IsNullOrWhiteSpace(correlationId))
            {
                kafkaMessage.Headers.Add("X-Correlation-ID", Encoding.UTF8.GetBytes(correlationId));
            }

            var deliveryResult = await _producer.ProduceAsync(topic, kafkaMessage, ct);
            _logger.LogInformation("Kafka published message to topic {Topic} [Partition: {Partition}, Offset: {Offset}, Key: {Key}]",
                topic, deliveryResult.Partition.Value, deliveryResult.Offset.Value, key);
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogError(ex, "Failed to publish Kafka message to topic {Topic} with Key: {Key}. Error: {Reason}",
                topic, key, ex.Error.Reason);
            throw;
        }
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
