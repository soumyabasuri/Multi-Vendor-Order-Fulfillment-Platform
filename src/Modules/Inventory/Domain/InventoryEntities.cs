using MultiVendorPlatform.Modules.Catalog.Domain;
using MultiVendorPlatform.Modules.Common.Domain;

namespace MultiVendorPlatform.Modules.Inventory.Domain;

public class Inventory : AggregateRoot<Guid>
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public Guid? ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }

    public int Quantity { get; set; }
    public int ReservedQuantity { get; set; }
    public int AvailableQuantity => Quantity - ReservedQuantity;

    public int ReorderThreshold { get; set; } = 5;
    public uint Version { get; set; } // Concurrency token / optimistic lock

    public ICollection<InventoryReservation> Reservations { get; set; } = new List<InventoryReservation>();

    public Inventory()
    {
        Id = Guid.NewGuid();
    }
}

public enum ReservationStatus
{
    Reserved = 0,
    Committed = 1,
    Released = 2,
    Expired = 3
}

public class InventoryReservation : BaseEntity<Guid>
{
    public Guid InventoryId { get; set; }
    public Inventory Inventory { get; set; } = null!;

    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }

    public int Quantity { get; set; }
    public ReservationStatus Status { get; set; } = ReservationStatus.Reserved;
    public DateTime ExpiresAtUtc { get; set; }

    public InventoryReservation()
    {
        Id = Guid.NewGuid();
    }
}
