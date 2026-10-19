using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MultiVendorPlatform.Infrastructure.Kafka;

public interface IRetryAndDeadLetterHandler
{
    Task ExecuteWithRetryAndDlqAsync(
        string topic,
        string key,
        string payloadJson,
        Func<Task> messageHandler,
        CancellationToken ct = default);
}

public class RetryAndDeadLetterHandler : IRetryAndDeadLetterHandler
{
    private readonly IKafkaProducer _kafkaProducer;
    private readonly ILogger<RetryAndDeadLetterHandler> _logger;
    private const int MaxRetries = 3;

    public RetryAndDeadLetterHandler(IKafkaProducer kafkaProducer, ILogger<RetryAndDeadLetterHandler> logger)
    {
        _kafkaProducer = kafkaProducer;
        _logger = logger;
    }

    public async Task ExecuteWithRetryAndDlqAsync(
        string topic,
        string key,
        string payloadJson,
        Func<Task> messageHandler,
        CancellationToken ct = default)
    {
        int attempt = 0;
        var delays = new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) };

        while (true)
        {
            attempt++;
            try
            {
                await messageHandler();
                return; // Successful processing
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Consumer processing failed on topic {Topic} for key {Key}. Attempt {Attempt}/{MaxRetries}",
                    topic, key, attempt, MaxRetries);

                if (attempt <= MaxRetries)
                {
                    var backoff = delays[attempt - 1];
                    _logger.LogInformation("Retrying topic {Topic} in {DelaySeconds}s...", topic, backoff.TotalSeconds);
                    await Task.Delay(backoff, ct);
                }
                else
                {
                    // Exceeded max retries: send to Dead Letter Queue
                    _logger.LogError(ex, "Max retry attempts exceeded for topic {Topic}, key {Key}. Forwarding to Dead Letter Topic: {DlqTopic}",
                        topic, key, KafkaTopics.DeadLetter);

                    var deadLetterEvent = new DeadLetterEvent(
                        topic,
                        "Unknown",
                        payloadJson,
                        ex.Message,
                        attempt,
                        DateTime.UtcNow);

                    try
                    {
                        await _kafkaProducer.PublishAsync(
                            KafkaTopics.DeadLetter,
                            key,
                            deadLetterEvent,
                            null,
                            ct);
                    }
                    catch (Exception dlqEx)
                    {
                        _logger.LogCritical(dlqEx, "CRITICAL: Failed to publish message to Dead Letter Queue! Topic: {Topic}, Key: {Key}", topic, key);
                    }

                    return; // Return so the consumer offset can commit and not permanently poison the consumer group
                }
            }
        }
    }
}
