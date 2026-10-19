using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MultiVendorPlatform.Infrastructure.Data;
using MultiVendorPlatform.Infrastructure.Kafka;
using MultiVendorPlatform.Infrastructure.Outbox;
using MultiVendorPlatform.Modules.Common.Models;
using MultiVendorPlatform.Modules.Common.Results;
using MultiVendorPlatform.Modules.Identity.Domain;
using MultiVendorPlatform.Modules.Inventory.DTOs;
using MultiVendorPlatform.Modules.Inventory.Services;
using MultiVendorPlatform.Modules.Orders.Domain;
using MultiVendorPlatform.Modules.Orders.DTOs;

namespace MultiVendorPlatform.Modules.Orders.Services;

public interface IOrderService
{
    Task<Result<OrderDto>> CreateOrderAsync(Guid customerId, CreateOrderRequest request, CancellationToken ct = default);
    Task<Result<OrderDto>> GetOrderByIdAsync(Guid orderId, Guid requestingUserId, string role, CancellationToken ct = default);
    Task<Result<PagedResult<OrderSummaryDto>>> GetCustomerOrdersAsync(Guid customerId, int pageNumber, int pageSize, CancellationToken ct = default);
    Task<Result<PagedResult<VendorOrderDto>>> GetVendorOrdersAsync(Guid vendorId, int pageNumber, int pageSize, CancellationToken ct = default);
    Task<Result> CancelOrderAsync(Guid orderId, Guid requestingUserId, string reason, CancellationToken ct = default);
}

public class OrderService : IOrderService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IInventoryService _inventoryService;
    private readonly IOutboxService _outboxService;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        ApplicationDbContext dbContext,
        IInventoryService inventoryService,
        IOutboxService outboxService,
        ILogger<OrderService> logger)
    {
        _dbContext = dbContext;
        _inventoryService = inventoryService;
        _outboxService = outboxService;
        _logger = logger;
    }

    public async Task<Result<OrderDto>> CreateOrderAsync(Guid customerId, CreateOrderRequest request, CancellationToken ct = default)
    {
        var cart = await _dbContext.Carts
            .Include(c => c.Items)
            .ThenInclude(i => i.Product)
            .ThenInclude(p => p.Vendor)
            .Include(c => c.Items)
            .ThenInclude(i => i.ProductVariant)
            .FirstOrDefaultAsync(c => c.CustomerId == customerId, ct);

        if (cart == null || !cart.Items.Any())
        {
            return Result.Failure<OrderDto>(Error.Validation("Cart.Empty", "Cannot create an order from an empty cart."));
        }

        // Verify all items are active
        foreach (var item in cart.Items)
        {
            if (!item.Product.IsActive)
            {
                return Result.Failure<OrderDto>(Error.Validation("Cart.InactiveProduct", $"Product '{item.Product.Name}' is no longer active."));
            }
        }

        var orderId = Guid.NewGuid();
        var orderNumber = Order.GenerateOrderNumber();

        // Multi-Vendor grouping: group items by vendor
        var itemsByVendor = cart.Items.GroupBy(i => i.Product.VendorId).ToList();

        // Execute in Database Transaction
        await using var tx = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        try
        {
            // 1. Concurrently reserve inventory for all items
            var reserveItems = cart.Items.Select(i => new ReserveStockItem(i.ProductId, i.ProductVariantId, i.Quantity)).ToList();
            var reservationResult = await _inventoryService.ReserveInventoryAsync(orderId, reserveItems, TimeSpan.FromMinutes(15), ct);
            if (reservationResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<OrderDto>(reservationResult.Error);
            }

            // 2. Calculate amounts
            decimal subtotal = cart.Items.Sum(i => i.Quantity * i.UnitPrice);
            decimal shipping = subtotal >= 100m ? 0m : 10m; // Free shipping over $100
            decimal tax = Math.Round(subtotal * 0.05m, 2);  // 5% estimated tax
            decimal total = subtotal + shipping + tax;

            var shippingAddressJson = JsonSerializer.Serialize(request.ShippingAddress);

            var order = new Order
            {
                OrderNumber = orderNumber,
                CustomerId = customerId,
                Status = OrderStatus.InventoryReserved,
                SubtotalAmount = subtotal,
                ShippingAmount = shipping,
                TaxAmount = tax,
                TotalAmount = total,
                ShippingAddressJson = shippingAddressJson,
                CreatedAtUtc = DateTime.UtcNow
            };

            // 3. Create OrderItems & VendorOrders
            var orderItemEventDtos = new List<OrderItemEventDto>();

            foreach (var vendorGroup in itemsByVendor)
            {
                var vendor = vendorGroup.First().Product.Vendor;
                var vendorSubtotal = vendorGroup.Sum(i => i.Quantity * i.UnitPrice);
                var commission = Math.Round(vendorSubtotal * vendor.CommissionRate, 2);

                var vendorOrder = new VendorOrder
                {
                    OrderId = order.Id,
                    VendorId = vendor.Id,
                    Status = VendorOrderStatus.Pending,
                    SubtotalAmount = vendorSubtotal,
                    CommissionAmount = commission
                };

                order.VendorOrders.Add(vendorOrder);

                foreach (var item in vendorGroup)
                {
                    var orderItem = new OrderItem
                    {
                        OrderId = order.Id,
                        VendorId = vendor.Id,
                        ProductId = item.ProductId,
                        ProductVariantId = item.ProductVariantId,
                        ProductName = item.Product.Name,
                        Sku = item.ProductVariant != null ? item.ProductVariant.Sku : item.Product.Sku,
                        Quantity = item.Quantity,
                        UnitPrice = item.UnitPrice,
                        TotalPrice = item.Quantity * item.UnitPrice
                    };

                    order.Items.Add(orderItem);

                    orderItemEventDtos.Add(new OrderItemEventDto(
                        item.ProductId,
                        item.ProductVariantId,
                        vendor.Id,
                        item.Quantity,
                        item.UnitPrice));
                }
            }

            _dbContext.Orders.Add(order);

            // 4. Outbox: Enqueue OrderCreated and InventoryReserved events
            var orderCreatedEvent = new OrderCreatedEvent(
                order.Id,
                order.OrderNumber,
                order.CustomerId,
                order.TotalAmount,
                orderItemEventDtos,
                DateTime.UtcNow);

            var inventoryReservedEvent = new InventoryReservedEvent(
                order.Id,
                orderItemEventDtos,
                DateTime.UtcNow.AddMinutes(15));

            _outboxService.Enqueue(orderCreatedEvent, KafkaTopics.OrderCreated, nameof(OrderCreatedEvent), "Order", order.Id.ToString());
            _outboxService.Enqueue(inventoryReservedEvent, KafkaTopics.InventoryReserved, nameof(InventoryReservedEvent), "Order", order.Id.ToString());

            // 5. Clear Cart Items
            _dbContext.CartItems.RemoveRange(cart.Items);

            await _dbContext.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation("Order {OrderNumber} ({OrderId}) successfully created with {VendorCount} vendor splits.",
                order.OrderNumber, order.Id, order.VendorOrders.Count);

            return await GetOrderByIdInternalAsync(order.Id, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create order for customer {CustomerId}", customerId);
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<Result<OrderDto>> GetOrderByIdAsync(Guid orderId, Guid requestingUserId, string role, CancellationToken ct = default)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .ThenInclude(i => i.Vendor)
            .Include(o => o.VendorOrders)
            .ThenInclude(vo => vo.Vendor)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        if (order == null)
        {
            return Result.Failure<OrderDto>(Error.NotFound("Order.NotFound", "Order not found."));
        }

        // Authorization check: Customer can see their own, Vendor can see their own, Admin can see all
        if (role == Role.Customer && order.CustomerId != requestingUserId)
        {
            return Result.Failure<OrderDto>(Error.Forbidden("Order.Forbidden", "You do not have access to this order."));
        }

        return Result.Success(MapToDto(order));
    }

    public async Task<Result<PagedResult<OrderSummaryDto>>> GetCustomerOrdersAsync(Guid customerId, int pageNumber, int pageSize, CancellationToken ct = default)
    {
        var query = _dbContext.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAtUtc);

        var totalCount = await query.CountAsync(ct);
        var orders = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new OrderSummaryDto(
                o.Id,
                o.OrderNumber,
                o.CustomerId,
                o.Status,
                o.TotalAmount,
                o.Items.Sum(i => i.Quantity),
                o.VendorOrders.Count,
                o.CreatedAtUtc))
            .ToListAsync(ct);

        return Result.Success(new PagedResult<OrderSummaryDto>(orders, totalCount, pageNumber, pageSize));
    }

    public async Task<Result<PagedResult<VendorOrderDto>>> GetVendorOrdersAsync(Guid vendorId, int pageNumber, int pageSize, CancellationToken ct = default)
    {
        var query = _dbContext.VendorOrders
            .AsNoTracking()
            .Include(vo => vo.Vendor)
            .Include(vo => vo.Order)
            .ThenInclude(o => o.Items)
            .Where(vo => vo.VendorId == vendorId)
            .OrderByDescending(vo => vo.CreatedAtUtc);

        var totalCount = await query.CountAsync(ct);
        var vendorOrders = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var dtos = vendorOrders.Select(vo =>
        {
            var vendorItems = vo.Order.Items.Where(i => i.VendorId == vendorId).Select(i => new OrderItemDto(
                i.Id,
                i.VendorId,
                vo.Vendor.BusinessName,
                i.ProductId,
                i.ProductName,
                i.Sku,
                i.ProductVariantId,
                i.Quantity,
                i.UnitPrice,
                i.TotalPrice)).ToList();

            return new VendorOrderDto(
                vo.Id,
                vo.VendorId,
                vo.Vendor.BusinessName,
                vo.Status,
                vo.SubtotalAmount,
                vo.CommissionAmount,
                vo.PayoutAmount,
                vo.TrackingNumber,
                vo.ShippedAtUtc,
                vo.DeliveredAtUtc,
                vendorItems);
        }).ToList();

        return Result.Success(new PagedResult<VendorOrderDto>(dtos, totalCount, pageNumber, pageSize));
    }

    public async Task<Result> CancelOrderAsync(Guid orderId, Guid requestingUserId, string reason, CancellationToken ct = default)
    {
        await using var tx = await _dbContext.Database.BeginTransactionAsync(ct);

        var order = await _dbContext.Orders
            .Include(o => o.VendorOrders)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        if (order == null)
        {
            return Result.Failure(Error.NotFound("Order.NotFound", "Order not found."));
        }

        // Allowed states to cancel: Pending, InventoryReserved, PaymentPending, Confirmed
        if (order.Status is OrderStatus.Shipped or OrderStatus.Delivered or OrderStatus.Cancelled or OrderStatus.Failed)
        {
            return Result.Failure(Error.Conflict("Order.CannotCancel", $"Order in status '{order.Status}' cannot be cancelled."));
        }

        order.Status = OrderStatus.Cancelled;
        order.CancellationReason = reason;
        order.UpdatedAtUtc = DateTime.UtcNow;

        foreach (var vo in order.VendorOrders)
        {
            vo.Status = VendorOrderStatus.Cancelled;
            vo.UpdatedAtUtc = DateTime.UtcNow;
        }

        // Release inventory reservation
        await _inventoryService.ReleaseReservationAsync(orderId, ct);

        // Outbox event
        var inventoryReleasedEvent = new InventoryReleasedEvent(orderId, $"Order cancelled: {reason}", DateTime.UtcNow);
        _outboxService.Enqueue(inventoryReleasedEvent, KafkaTopics.InventoryReleased, nameof(InventoryReleasedEvent), "Order", orderId.ToString());

        await _dbContext.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("Order {OrderId} cancelled by user {UserId}. Reason: {Reason}", orderId, requestingUserId, reason);
        return Result.Success();
    }

    private async Task<Result<OrderDto>> GetOrderByIdInternalAsync(Guid orderId, CancellationToken ct)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .ThenInclude(i => i.Vendor)
            .Include(o => o.VendorOrders)
            .ThenInclude(vo => vo.Vendor)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        return order == null
            ? Result.Failure<OrderDto>(Error.NotFound("Order.NotFound", "Order not found."))
            : Result.Success(MapToDto(order));
    }

    private static OrderDto MapToDto(Order o)
    {
        var shippingAddress = JsonSerializer.Deserialize<ShippingAddressDto>(o.ShippingAddressJson)
            ?? new ShippingAddressDto("", "", "", "", "", "", "");

        var items = o.Items.Select(i => new OrderItemDto(
            i.Id,
            i.VendorId,
            i.Vendor.BusinessName,
            i.ProductId,
            i.ProductName,
            i.Sku,
            i.ProductVariantId,
            i.Quantity,
            i.UnitPrice,
            i.TotalPrice)).ToList();

        var vendorOrders = o.VendorOrders.Select(vo =>
        {
            var vendorItems = items.Where(i => i.VendorId == vo.VendorId).ToList();
            return new VendorOrderDto(
                vo.Id,
                vo.VendorId,
                vo.Vendor.BusinessName,
                vo.Status,
                vo.SubtotalAmount,
                vo.CommissionAmount,
                vo.PayoutAmount,
                vo.TrackingNumber,
                vo.ShippedAtUtc,
                vo.DeliveredAtUtc,
                vendorItems);
        }).ToList();

        return new OrderDto(
            o.Id,
            o.OrderNumber,
            o.CustomerId,
            o.Status,
            o.SubtotalAmount,
            o.ShippingAmount,
            o.TaxAmount,
            o.TotalAmount,
            shippingAddress,
            o.CancellationReason,
            o.CreatedAtUtc,
            vendorOrders,
            items);
    }
}
