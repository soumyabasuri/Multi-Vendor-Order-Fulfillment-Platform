namespace MultiVendorPlatform.Infrastructure.Kafka;

public static class KafkaTopics
{
    public const string OrderCreated = "orders.order-created";
    public const string InventoryReserved = "inventory.inventory-reserved";
    public const string InventoryReleased = "inventory.inventory-released";
    public const string PaymentInitiated = "payments.payment-initiated";
    public const string PaymentCompleted = "payments.payment-completed";
    public const string PaymentFailed = "payments.payment-failed";
    public const string OrderConfirmed = "orders.order-confirmed";
    public const string ShipmentCreated = "fulfillment.shipment-created";
    public const string OrderDelivered = "orders.order-delivered";
    public const string DeadLetter = "events.dead-letter-queue";
}

public record OrderItemEventDto(
    Guid ProductId,
    Guid? ProductVariantId,
    Guid VendorId,
    int Quantity,
    decimal UnitPrice);

public record OrderCreatedEvent(
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId,
    decimal TotalAmount,
    IReadOnlyList<OrderItemEventDto> Items,
    DateTime CreatedAtUtc);

public record InventoryReservedEvent(
    Guid OrderId,
    IReadOnlyList<OrderItemEventDto> Items,
    DateTime ExpiresAtUtc);

public record InventoryReleasedEvent(
    Guid OrderId,
    string Reason,
    DateTime ReleasedAtUtc);

public record PaymentInitiatedEvent(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string Currency,
    DateTime InitiatedAtUtc);

public record PaymentCompletedEvent(
    Guid PaymentId,
    Guid OrderId,
    string TransactionReference,
    decimal Amount,
    DateTime CompletedAtUtc);

public record PaymentFailedEvent(
    Guid PaymentId,
    Guid OrderId,
    string Reason,
    DateTime FailedAtUtc);

public record OrderConfirmedEvent(
    Guid OrderId,
    string OrderNumber,
    DateTime ConfirmedAtUtc);

public record ShipmentCreatedEvent(
    Guid ShipmentId,
    Guid OrderId,
    Guid VendorOrderId,
    Guid VendorId,
    string Carrier,
    string TrackingNumber,
    DateTime CreatedAtUtc);

public record OrderDeliveredEvent(
    Guid OrderId,
    Guid VendorOrderId,
    DateTime DeliveredAtUtc);

public record DeadLetterEvent(
    string OriginalTopic,
    string EventType,
    string PayloadJson,
    string ExceptionMessage,
    int RetryAttempts,
    DateTime FailedAtUtc);
