using MultiVendorPlatform.Modules.Common.Domain;
using MultiVendorPlatform.Modules.Orders.Domain;
using MultiVendorPlatform.Modules.Vendors.Domain;

namespace MultiVendorPlatform.Modules.Fulfillment.Domain;

public enum ShipmentStatus
{
    Created = 0,
    Picked = 1,
    Packed = 2,
    Dispatched = 3,
    InTransit = 4,
    Delivered = 5,
    Returned = 6
}

public class Shipment : AggregateRoot<Guid>
{
    public Guid VendorOrderId { get; set; }
    public VendorOrder VendorOrder { get; set; } = null!;

    public Guid OrderId { get; set; }
    public Guid VendorId { get; set; }
    public Vendor Vendor { get; set; } = null!;

    public string Carrier { get; set; } = "FedEx";
    public string TrackingNumber { get; set; } = string.Empty;
    public ShipmentStatus Status { get; set; } = ShipmentStatus.Created;

    public DateTime? ShippedAtUtc { get; set; }
    public DateTime? EstimatedDeliveryUtc { get; set; }
    public DateTime? ActualDeliveryUtc { get; set; }

    public string TrackingHistoryJson { get; set; } = "[]";

    public Shipment()
    {
        Id = Guid.NewGuid();
    }
}
