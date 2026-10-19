using System.Text.Json;
using Microsoft.Extensions.Logging;
using MultiVendorPlatform.Modules.Orders.Services;
using MultiVendorPlatform.Modules.Payments.Services;

namespace MultiVendorPlatform.Infrastructure.Kafka;

public class KafkaEventDispatcher : IKafkaEventDispatcher
{
    private readonly IOrderSagaCoordinator _sagaCoordinator;
    private readonly ILogger<KafkaEventDispatcher> _logger;

    public KafkaEventDispatcher(
        IOrderSagaCoordinator sagaCoordinator,
        ILogger<KafkaEventDispatcher> logger)
    {
        _sagaCoordinator = sagaCoordinator;
        _logger = logger;
    }

    public async Task DispatchAsync(string topic, string key, string payloadJson, CancellationToken ct = default)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        switch (topic)
        {
            case KafkaTopics.OrderCreated:
                var orderCreated = JsonSerializer.Deserialize<OrderCreatedEvent>(payloadJson, options);
                if (orderCreated != null)
                {
                    _logger.LogInformation("Dispatching OrderCreated event for Order {OrderId}", orderCreated.OrderId);
                    await _sagaCoordinator.HandleOrderCreatedAsync(orderCreated, ct);
                }
                break;

            case KafkaTopics.PaymentCompleted:
                var paymentCompleted = JsonSerializer.Deserialize<PaymentCompletedEvent>(payloadJson, options);
                if (paymentCompleted != null)
                {
                    _logger.LogInformation("Dispatching PaymentCompleted event for Order {OrderId}", paymentCompleted.OrderId);
                    await _sagaCoordinator.HandlePaymentCompletedAsync(paymentCompleted, ct);
                }
                break;

            case KafkaTopics.PaymentFailed:
                var paymentFailed = JsonSerializer.Deserialize<PaymentFailedEvent>(payloadJson, options);
                if (paymentFailed != null)
                {
                    _logger.LogInformation("Dispatching PaymentFailed event for Order {OrderId}", paymentFailed.OrderId);
                    await _sagaCoordinator.HandlePaymentFailedAsync(paymentFailed, ct);
                }
                break;

            default:
                _logger.LogInformation("No dedicated saga action needed for topic {Topic}, key {Key}", topic, key);
                break;
        }
    }
}
