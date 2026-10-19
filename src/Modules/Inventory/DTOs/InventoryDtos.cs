namespace MultiVendorPlatform.Modules.Inventory.DTOs;

public record AdjustStockRequest(
    Guid ProductId,
    Guid? ProductVariantId,
    int QuantityAdjustment, // Positive to add, negative to reduce
    string Reason);

public record ReserveStockItem(
    Guid ProductId,
    Guid? ProductVariantId,
    int Quantity);

public record InventoryDto(
    Guid Id,
    Guid ProductId,
    Guid? ProductVariantId,
    int Quantity,
    int ReservedQuantity,
    int AvailableQuantity,
    int ReorderThreshold,
    bool IsLowStock);
