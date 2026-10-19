using MultiVendorPlatform.Modules.Orders.Domain;

namespace MultiVendorPlatform.Modules.Orders.DTOs;

public record ShippingAddressDto(
    string Street,
    string City,
    string State,
    string PostalCode,
    string Country,
    string RecipientName,
    string RecipientPhone);

public record CreateOrderRequest(
    ShippingAddressDto ShippingAddress,
    string? Notes = null);

public record CancelOrderRequest(
    string Reason);

public record OrderItemDto(
    Guid Id,
    Guid VendorId,
    string VendorName,
    Guid ProductId,
    string ProductName,
    string Sku,
    Guid? ProductVariantId,
    int Quantity,
    decimal UnitPrice,
    decimal TotalPrice);

public record VendorOrderDto(
    Guid Id,
    Guid VendorId,
    string VendorName,
    VendorOrderStatus Status,
    decimal SubtotalAmount,
    decimal CommissionAmount,
    decimal PayoutAmount,
    string? TrackingNumber,
    DateTime? ShippedAtUtc,
    DateTime? DeliveredAtUtc,
    IReadOnlyList<OrderItemDto> Items);

public record OrderDto(
    Guid Id,
    string OrderNumber,
    Guid CustomerId,
    OrderStatus Status,
    decimal SubtotalAmount,
    decimal ShippingAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    ShippingAddressDto ShippingAddress,
    string? CancellationReason,
    DateTime CreatedAtUtc,
    IReadOnlyList<VendorOrderDto> VendorOrders,
    IReadOnlyList<OrderItemDto> Items);

public record OrderSummaryDto(
    Guid Id,
    string OrderNumber,
    Guid CustomerId,
    OrderStatus Status,
    decimal TotalAmount,
    int TotalItems,
    int TotalVendors,
    DateTime CreatedAtUtc);
