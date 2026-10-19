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
    private readonly IProducer<string, string>? _producer;
    private readonly ILogger<KafkaProducer> _logger;
    private readonly bool _isEnabled;

    public KafkaProducer(IConfiguration configuration, ILogger<KafkaProducer> logger)
    {
        _logger = logger;
        var bootstrapServers = configuration["Kafka:BootstrapServers"];
        if (string.IsNullOrWhiteSpace(bootstrapServers) || bootstrapServers.Equals("disabled", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Kafka:BootstrapServers is not configured or disabled. Kafka publishing is disabled.");
            _isEnabled = false;
            _producer = null;
            return;
        }

        _isEnabled = true;
        var config = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.All, // Strong durability guarantee
            EnableIdempotence = true, // Prevent duplicate writes at producer level
            MessageSendMaxRetries = 3,
            RetryBackoffMs = 100,
            LingerMs = 5
        };

        ApplyCloudSecuritySettings(config, configuration, bootstrapServers);

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
        if (!_isEnabled || _producer == null)
        {
            _logger.LogDebug("Kafka is disabled or not configured. Skipping message publish to {Topic}", topic);
            return;
        }

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

        var certLocation = configuration["Kafka:SslCertificateLocation"];
        if (!string.IsNullOrWhiteSpace(certLocation))
        {
            config.SslCertificateLocation = certLocation;
        }

        var certPem = configuration["Kafka:SslCertificatePem"];
        if (!string.IsNullOrWhiteSpace(certPem))
        {
            config.SslCertificatePem = certPem;
        }

        var keyLocation = configuration["Kafka:SslKeyLocation"];
        if (!string.IsNullOrWhiteSpace(keyLocation))
        {
            config.SslKeyLocation = keyLocation;
        }

        var keyPem = configuration["Kafka:SslKeyPem"];
        if (!string.IsNullOrWhiteSpace(keyPem))
        {
            config.SslKeyPem = keyPem;
        }

        var caLocation = configuration["Kafka:SslCaLocation"];
        if (!string.IsNullOrWhiteSpace(caLocation))
        {
            config.SslCaLocation = caLocation;
        }

        var caPem = configuration["Kafka:SslCaPem"];
        if (!string.IsNullOrWhiteSpace(caPem))
        {
            config.SslCaPem = caPem;
        }

        var enableSslVerificationStr = configuration["Kafka:EnableSslCertificateVerification"];
        if (bool.TryParse(enableSslVerificationStr, out var enableSsl))
        {
            config.EnableSslCertificateVerification = enableSsl;
        }
        else if (bootstrapServers.Contains("aivencloud.com"))
        {
            if (string.IsNullOrWhiteSpace(caLocation) && string.IsNullOrWhiteSpace(caPem))
            {
                config.EnableSslCertificateVerification = false;
            }
        }
    }

    public void Dispose()
    {
        _producer?.Flush(TimeSpan.FromSeconds(5));
        _producer?.Dispose();
    }
}
