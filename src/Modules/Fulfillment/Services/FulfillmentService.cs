using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MultiVendorPlatform.Infrastructure.Data;
using MultiVendorPlatform.Infrastructure.Kafka;
using MultiVendorPlatform.Infrastructure.Outbox;
using MultiVendorPlatform.Modules.Common.Results;
using MultiVendorPlatform.Modules.Fulfillment.Domain;
using MultiVendorPlatform.Modules.Fulfillment.DTOs;
using MultiVendorPlatform.Modules.Notifications.Domain;
using MultiVendorPlatform.Modules.Orders.Domain;

namespace MultiVendorPlatform.Modules.Fulfillment.Services;

public interface IFulfillmentService
{
    Task<Result<ShipmentDto>> CreateShipmentAsync(Guid vendorId, CreateShipmentRequest request, CancellationToken ct = default);
    Task<Result<ShipmentDto>> UpdateShipmentStatusAsync(Guid shipmentId, Guid vendorId, UpdateShipmentStatusRequest request, CancellationToken ct = default);
    Task<Result<ShipmentDto>> GetShipmentByIdAsync(Guid shipmentId, CancellationToken ct = default);
    Task<Result<ShipmentDto>> GetShipmentByVendorOrderIdAsync(Guid vendorOrderId, CancellationToken ct = default);
}

public class FulfillmentService : IFulfillmentService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IOutboxService _outboxService;
    private readonly ILogger<FulfillmentService> _logger;

    public FulfillmentService(
        ApplicationDbContext dbContext,
        IOutboxService outboxService,
        ILogger<FulfillmentService> logger)
    {
        _dbContext = dbContext;
        _outboxService = outboxService;
        _logger = logger;
    }

    public async Task<Result<ShipmentDto>> CreateShipmentAsync(Guid vendorId, CreateShipmentRequest request, CancellationToken ct = default)
    {
        var vendorOrder = await _dbContext.VendorOrders
            .Include(vo => vo.Vendor)
            .Include(vo => vo.Order)
            .ThenInclude(o => o.VendorOrders)
            .FirstOrDefaultAsync(vo => vo.Id == request.VendorOrderId, ct);

        if (vendorOrder == null)
        {
            return Result.Failure<ShipmentDto>(Error.NotFound("VendorOrder.NotFound", "Vendor order not found."));
        }

        if (vendorOrder.VendorId != vendorId)
        {
            return Result.Failure<ShipmentDto>(Error.Forbidden("Fulfillment.Forbidden", "You do not have access to fulfill this order."));
        }

        if (vendorOrder.Status is not (VendorOrderStatus.Preparing or VendorOrderStatus.Pending))
        {
            return Result.Failure<ShipmentDto>(Error.Conflict("Fulfillment.InvalidStatus",
                $"Cannot create shipment for order in status '{vendorOrder.Status}'."));
        }

        var trackingNumber = string.IsNullOrWhiteSpace(request.TrackingNumber)
            ? $"TRK-{request.Carrier[..Math.Min(3, request.Carrier.Length)].ToUpperInvariant()}-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}"
            : request.TrackingNumber;

        await using var tx = await _dbContext.Database.BeginTransactionAsync(ct);
        try
        {
            var initialHistory = new List<TrackingEventDto>
            {
                new("Created", "Vendor Warehouse", "Shipment information created and package packed.", DateTime.UtcNow),
                new("Dispatched", "Vendor Warehouse", "Package handed over to carrier.", DateTime.UtcNow)
            };

            var shipment = new Shipment
            {
                VendorOrderId = vendorOrder.Id,
                OrderId = vendorOrder.OrderId,
                VendorId = vendorId,
                Carrier = request.Carrier,
                TrackingNumber = trackingNumber,
                Status = ShipmentStatus.Dispatched,
                ShippedAtUtc = DateTime.UtcNow,
                EstimatedDeliveryUtc = request.EstimatedDeliveryUtc ?? DateTime.UtcNow.AddDays(3),
                TrackingHistoryJson = JsonSerializer.Serialize(initialHistory)
            };

            vendorOrder.Status = VendorOrderStatus.Shipped;
            vendorOrder.TrackingNumber = trackingNumber;
            vendorOrder.ShippedAtUtc = DateTime.UtcNow;
            vendorOrder.UpdatedAtUtc = DateTime.UtcNow;

            // Check parent order state: are all vendor orders shipped?
            var allVendorOrders = vendorOrder.Order.VendorOrders;
            bool allShipped = allVendorOrders.All(vo => vo.Id == vendorOrder.Id || vo.Status == VendorOrderStatus.Shipped || vo.Status == VendorOrderStatus.Delivered);

            vendorOrder.Order.Status = allShipped ? OrderStatus.Shipped : OrderStatus.PartiallyShipped;
            vendorOrder.Order.UpdatedAtUtc = DateTime.UtcNow;

            _dbContext.Shipments.Add(shipment);

            // Outbox event
            var shipmentCreatedEvent = new ShipmentCreatedEvent(
                shipment.Id,
                vendorOrder.OrderId,
                vendorOrder.Id,
                vendorId,
                shipment.Carrier,
                shipment.TrackingNumber,
                DateTime.UtcNow);

            _outboxService.Enqueue(shipmentCreatedEvent, KafkaTopics.ShipmentCreated, nameof(ShipmentCreatedEvent), "Shipment", shipment.Id.ToString());

            // Notification to customer
            var notification = new Notification
            {
                UserId = vendorOrder.Order.CustomerId,
                Type = NotificationType.InApp,
                Title = "Item Shipped!",
                Message = $"Your package from {vendorOrder.Vendor.BusinessName} has shipped with {shipment.Carrier}! Tracking number: {shipment.TrackingNumber}",
                Status = NotificationStatus.Sent,
                SentAtUtc = DateTime.UtcNow
            };
            _dbContext.Notifications.Add(notification);

            await _dbContext.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation("Shipment {ShipmentId} created for VendorOrder {VendorOrderId}. Tracking: {Tracking}",
                shipment.Id, vendorOrder.Id, trackingNumber);

            return Result.Success(MapToDto(shipment));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create shipment for VendorOrder {VendorOrderId}", request.VendorOrderId);
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<Result<ShipmentDto>> UpdateShipmentStatusAsync(Guid shipmentId, Guid vendorId, UpdateShipmentStatusRequest request, CancellationToken ct = default)
    {
        var shipment = await _dbContext.Shipments
            .Include(s => s.VendorOrder)
            .ThenInclude(vo => vo.Order)
            .ThenInclude(o => o.VendorOrders)
            .FirstOrDefaultAsync(s => s.Id == shipmentId, ct);

        if (shipment == null)
        {
            return Result.Failure<ShipmentDto>(Error.NotFound("Shipment.NotFound", "Shipment not found."));
        }

        if (shipment.VendorId != vendorId)
        {
            return Result.Failure<ShipmentDto>(Error.Forbidden("Shipment.Forbidden", "You do not have access to update this shipment."));
        }

        await using var tx = await _dbContext.Database.BeginTransactionAsync(ct);
        try
        {
            shipment.Status = request.Status;
            shipment.UpdatedAtUtc = DateTime.UtcNow;

            var history = JsonSerializer.Deserialize<List<TrackingEventDto>>(shipment.TrackingHistoryJson) ?? new List<TrackingEventDto>();
            history.Add(new TrackingEventDto(
                request.Status.ToString(),
                request.Location ?? "Distribution Center",
                request.StatusNote ?? $"Status updated to {request.Status}",
                DateTime.UtcNow));

            shipment.TrackingHistoryJson = JsonSerializer.Serialize(history);

            if (request.Status == ShipmentStatus.Delivered)
            {
                shipment.ActualDeliveryUtc = DateTime.UtcNow;
                shipment.VendorOrder.Status = VendorOrderStatus.Delivered;
                shipment.VendorOrder.DeliveredAtUtc = DateTime.UtcNow;
                shipment.VendorOrder.UpdatedAtUtc = DateTime.UtcNow;

                // Check if all vendor orders are delivered
                bool allDelivered = shipment.VendorOrder.Order.VendorOrders.All(vo =>
                    vo.Id == shipment.VendorOrderId || vo.Status == VendorOrderStatus.Delivered);

                if (allDelivered)
                {
                    shipment.VendorOrder.Order.Status = OrderStatus.Delivered;
                    shipment.VendorOrder.Order.UpdatedAtUtc = DateTime.UtcNow;
                }

                // Outbox
                var deliveredEvent = new OrderDeliveredEvent(shipment.OrderId, shipment.VendorOrderId, DateTime.UtcNow);
                _outboxService.Enqueue(deliveredEvent, KafkaTopics.OrderDelivered, nameof(OrderDeliveredEvent), "Order", shipment.OrderId.ToString());

                // Notification to customer
                var notification = new Notification
                {
                    UserId = shipment.VendorOrder.Order.CustomerId,
                    Type = NotificationType.InApp,
                    Title = "Package Delivered",
                    Message = $"Your package with tracking number {shipment.TrackingNumber} has been delivered!",
                    Status = NotificationStatus.Sent,
                    SentAtUtc = DateTime.UtcNow
                };
                _dbContext.Notifications.Add(notification);
            }

            await _dbContext.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Result.Success(MapToDto(shipment));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update status for Shipment {ShipmentId}", shipmentId);
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<Result<ShipmentDto>> GetShipmentByIdAsync(Guid shipmentId, CancellationToken ct = default)
    {
        var shipment = await _dbContext.Shipments.AsNoTracking().FirstOrDefaultAsync(s => s.Id == shipmentId, ct);
        return shipment == null
            ? Result.Failure<ShipmentDto>(Error.NotFound("Shipment.NotFound", "Shipment not found."))
            : Result.Success(MapToDto(shipment));
    }

    public async Task<Result<ShipmentDto>> GetShipmentByVendorOrderIdAsync(Guid vendorOrderId, CancellationToken ct = default)
    {
        var shipment = await _dbContext.Shipments.AsNoTracking()
            .OrderByDescending(s => s.CreatedAtUtc)
            .FirstOrDefaultAsync(s => s.VendorOrderId == vendorOrderId, ct);

        return shipment == null
            ? Result.Failure<ShipmentDto>(Error.NotFound("Shipment.NotFound", "Shipment not found for vendor order."))
            : Result.Success(MapToDto(shipment));
    }

    private static ShipmentDto MapToDto(Shipment s)
    {
        var history = JsonSerializer.Deserialize<List<TrackingEventDto>>(s.TrackingHistoryJson)
            ?? new List<TrackingEventDto>();

        return new ShipmentDto(
            s.Id,
            s.VendorOrderId,
            s.OrderId,
            s.VendorId,
            s.Carrier,
            s.TrackingNumber,
            s.Status,
            s.ShippedAtUtc,
            s.EstimatedDeliveryUtc,
            s.ActualDeliveryUtc,
            history,
            s.CreatedAtUtc);
    }
}
