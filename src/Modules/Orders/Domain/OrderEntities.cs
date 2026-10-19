using MultiVendorPlatform.Modules.Catalog.Domain;
using MultiVendorPlatform.Modules.Common.Domain;
using MultiVendorPlatform.Modules.Identity.Domain;
using MultiVendorPlatform.Modules.Vendors.Domain;

namespace MultiVendorPlatform.Modules.Orders.Domain;

public enum OrderStatus
{
    Pending = 0,
    InventoryReserved = 1,
    PaymentPending = 2,
    Confirmed = 3,
    Processing = 4,
    PartiallyShipped = 5,
    Shipped = 6,
    Delivered = 7,
    Cancelled = 8,
    Failed = 9
}

public enum VendorOrderStatus
{
    Pending = 0,
    Preparing = 1,
    ReadyForPickup = 2,
    Shipped = 3,
    Delivered = 4,
    Cancelled = 5
}

public class Order : AggregateRoot<Guid>
{
    public string OrderNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public User Customer { get; set; } = null!;

    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public decimal SubtotalAmount { get; set; }
    public decimal ShippingAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }

    public string ShippingAddressJson { get; set; } = "{}";
    public string? CancellationReason { get; set; }

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public ICollection<VendorOrder> VendorOrders { get; set; } = new List<VendorOrder>();

    public Order()
    {
        Id = Guid.NewGuid();
    }

    public static string GenerateOrderNumber() =>
        $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
}

public class OrderItem : BaseEntity<Guid>
{
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public Guid VendorId { get; set; }
    public Vendor Vendor { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public Guid? ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }

    public string ProductName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }

    public OrderItem()
    {
        Id = Guid.NewGuid();
    }
}

public class VendorOrder : BaseEntity<Guid>
{
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public Guid VendorId { get; set; }
    public Vendor Vendor { get; set; } = null!;

    public VendorOrderStatus Status { get; set; } = VendorOrderStatus.Pending;
    public decimal SubtotalAmount { get; set; }
    public decimal CommissionAmount { get; set; }
    public decimal PayoutAmount => SubtotalAmount - CommissionAmount;

    public string? TrackingNumber { get; set; }
    public DateTime? ShippedAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }

    public VendorOrder()
    {
        Id = Guid.NewGuid();
    }
}
