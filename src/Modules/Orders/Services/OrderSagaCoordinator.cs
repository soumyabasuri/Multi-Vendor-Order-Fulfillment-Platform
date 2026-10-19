using Microsoft.EntityFrameworkCore;
using MultiVendorPlatform.Infrastructure.Data;
using MultiVendorPlatform.Infrastructure.Kafka;
using MultiVendorPlatform.Infrastructure.Outbox;
using MultiVendorPlatform.Modules.Inventory.Services;
using MultiVendorPlatform.Modules.Notifications.Domain;
using MultiVendorPlatform.Modules.Orders.Domain;

namespace MultiVendorPlatform.Modules.Orders.Services;

public interface IOrderSagaCoordinator
{
    Task HandleOrderCreatedAsync(OrderCreatedEvent e, CancellationToken ct = default);
    Task HandlePaymentCompletedAsync(PaymentCompletedEvent e, CancellationToken ct = default);
    Task HandlePaymentFailedAsync(PaymentFailedEvent e, CancellationToken ct = default);
}

public class OrderSagaCoordinator : IOrderSagaCoordinator
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IInventoryService _inventoryService;
    private readonly IOutboxService _outboxService;
    private readonly ILogger<OrderSagaCoordinator> _logger;

    public OrderSagaCoordinator(
        ApplicationDbContext dbContext,
        IInventoryService inventoryService,
        IOutboxService outboxService,
        ILogger<OrderSagaCoordinator> logger)
    {
        _dbContext = dbContext;
        _inventoryService = inventoryService;
        _outboxService = outboxService;
        _logger = logger;
    }

    public async Task HandleOrderCreatedAsync(OrderCreatedEvent e, CancellationToken ct = default)
    {
        _logger.LogInformation("Saga: Processing OrderCreated for Order {OrderId} ({OrderNumber})", e.OrderId, e.OrderNumber);

        // Notify customer that order was received
        var notification = new Notification
        {
            UserId = e.CustomerId,
            Type = NotificationType.InApp,
            Title = "Order Placed",
            Message = $"Your order {e.OrderNumber} for ${e.TotalAmount:F2} has been placed. Please complete payment.",
            Status = NotificationStatus.Sent,
            SentAtUtc = DateTime.UtcNow
        };

        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task HandlePaymentCompletedAsync(PaymentCompletedEvent e, CancellationToken ct = default)
    {
        _logger.LogInformation("Saga: Handling PaymentCompleted for Order {OrderId}", e.OrderId);

        await using var tx = await _dbContext.Database.BeginTransactionAsync(ct);
        try
        {
            var order = await _dbContext.Orders
                .Include(o => o.VendorOrders)
                .ThenInclude(vo => vo.Vendor)
                .FirstOrDefaultAsync(o => o.Id == e.OrderId, ct);

            if (order == null)
            {
                _logger.LogError("Saga error: Order {OrderId} not found for completed payment.", e.OrderId);
                await tx.RollbackAsync(ct);
                return;
            }

            // Idempotency: If already confirmed, skip
            if (order.Status is OrderStatus.Confirmed or OrderStatus.Processing or OrderStatus.Shipped or OrderStatus.Delivered)
            {
                _logger.LogInformation("Saga: Order {OrderId} already in state {Status}. Skipping duplicate confirmation.", order.Id, order.Status);
                await tx.RollbackAsync(ct);
                return;
            }

            // 1. Commit inventory reservation
            var commitResult = await _inventoryService.CommitReservationAsync(order.Id, ct);
            if (commitResult.IsFailure)
            {
                _logger.LogError("Failed to commit inventory reservation for Order {OrderId}: {Error}", order.Id, commitResult.Error.Message);
            }

            // 2. Advance Order State to Confirmed
            order.Status = OrderStatus.Confirmed;
            order.UpdatedAtUtc = DateTime.UtcNow;

            // 3. Advance VendorOrders to Preparing (Pick/Pack ready)
            foreach (var vo in order.VendorOrders)
            {
                vo.Status = VendorOrderStatus.Preparing;
                vo.UpdatedAtUtc = DateTime.UtcNow;

                // Notify vendor
                var vendorNotification = new Notification
                {
                    UserId = vo.Vendor.OwnerUserId,
                    Type = NotificationType.InApp,
                    Title = "New Order to Fulfill",
                    Message = $"You have a new fulfillment order for Order {order.OrderNumber} (Subtotal: ${vo.SubtotalAmount:F2}).",
                    Status = NotificationStatus.Sent,
                    SentAtUtc = DateTime.UtcNow
                };
                _dbContext.Notifications.Add(vendorNotification);
            }

            // 4. Notify customer
            var customerNotification = new Notification
            {
                UserId = order.CustomerId,
                Type = NotificationType.InApp,
                Title = "Payment Confirmed",
                Message = $"Payment of ${e.Amount:F2} for order {order.OrderNumber} was successful. Vendors are preparing your shipment.",
                Status = NotificationStatus.Sent,
                SentAtUtc = DateTime.UtcNow
            };
            _dbContext.Notifications.Add(customerNotification);

            // 5. Outbox: OrderConfirmed
            var orderConfirmedEvent = new OrderConfirmedEvent(order.Id, order.OrderNumber, DateTime.UtcNow);
            _outboxService.Enqueue(orderConfirmedEvent, KafkaTopics.OrderConfirmed, nameof(OrderConfirmedEvent), "Order", order.Id.ToString());

            await _dbContext.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation("Saga: Order {OrderId} successfully confirmed and fulfillment tasks dispatched.", order.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Saga: Error confirming Order {OrderId}", e.OrderId);
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task HandlePaymentFailedAsync(PaymentFailedEvent e, CancellationToken ct = default)
    {
        _logger.LogWarning("Saga: Handling PaymentFailed for Order {OrderId}. Reason: {Reason}", e.OrderId, e.Reason);

        await using var tx = await _dbContext.Database.BeginTransactionAsync(ct);
        try
        {
            var order = await _dbContext.Orders
                .Include(o => o.VendorOrders)
                .FirstOrDefaultAsync(o => o.Id == e.OrderId, ct);

            if (order == null)
            {
                _logger.LogError("Saga error: Order {OrderId} not found for failed payment.", e.OrderId);
                await tx.RollbackAsync(ct);
                return;
            }

            // Idempotency: If already marked failed/cancelled, skip
            if (order.Status is OrderStatus.Failed or OrderStatus.Cancelled)
            {
                _logger.LogInformation("Saga: Order {OrderId} already failed/cancelled. Skipping.", order.Id);
                await tx.RollbackAsync(ct);
                return;
            }

            // 1. Compensating Transaction: Release reserved inventory
            var releaseResult = await _inventoryService.ReleaseReservationAsync(order.Id, ct);
            if (releaseResult.IsFailure)
            {
                _logger.LogError("Failed to release inventory during payment failure compensation for Order {OrderId}: {Error}",
                    order.Id, releaseResult.Error.Message);
            }

            // 2. Mark Order & VendorOrders as Failed
            order.Status = OrderStatus.Failed;
            order.CancellationReason = $"Payment failed: {e.Reason}";
            order.UpdatedAtUtc = DateTime.UtcNow;

            foreach (var vo in order.VendorOrders)
            {
                vo.Status = VendorOrderStatus.Cancelled;
                vo.UpdatedAtUtc = DateTime.UtcNow;
            }

            // 3. Notify Customer
            var notification = new Notification
            {
                UserId = order.CustomerId,
                Type = NotificationType.InApp,
                Title = "Payment Failed",
                Message = $"Payment failed for order {order.OrderNumber}. Any reserved inventory has been released. Please try again.",
                Status = NotificationStatus.Sent,
                SentAtUtc = DateTime.UtcNow
            };
            _dbContext.Notifications.Add(notification);

            // 4. Outbox: InventoryReleased
            var releasedEvent = new InventoryReleasedEvent(order.Id, $"Payment failed: {e.Reason}", DateTime.UtcNow);
            _outboxService.Enqueue(releasedEvent, KafkaTopics.InventoryReleased, nameof(InventoryReleasedEvent), "Order", order.Id.ToString());

            await _dbContext.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation("Saga: Order {OrderId} failed and compensation completed.", order.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Saga: Error executing compensation for Order {OrderId}", e.OrderId);
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
