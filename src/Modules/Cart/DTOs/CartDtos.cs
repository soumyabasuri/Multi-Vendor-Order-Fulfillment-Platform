namespace MultiVendorPlatform.Modules.Cart.DTOs;

public record AddCartItemRequest(
    Guid ProductId,
    Guid? ProductVariantId,
    int Quantity);

public record UpdateCartItemRequest(
    int Quantity);

public record CartItemDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductSku,
    Guid? ProductVariantId,
    string? VariantName,
    Guid VendorId,
    string VendorName,
    int Quantity,
    decimal UnitPrice,
    decimal TotalPrice,
    bool IsAvailable,
    int AvailableStock);

public record CartDto(
    Guid Id,
    Guid CustomerId,
    IReadOnlyList<CartItemDto> Items,
    decimal Subtotal,
    int TotalItems,
    bool HasStockIssues);
