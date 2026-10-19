using MultiVendorPlatform.Modules.Fulfillment.Domain;

namespace MultiVendorPlatform.Modules.Fulfillment.DTOs;

public record CreateShipmentRequest(
    Guid VendorOrderId,
    string Carrier,
    string? TrackingNumber = null,
    DateTime? EstimatedDeliveryUtc = null);

public record UpdateShipmentStatusRequest(
    ShipmentStatus Status,
    string? Location = null,
    string? StatusNote = null);

public record TrackingEventDto(
    string Status,
    string Location,
    string Note,
    DateTime TimestampUtc);

public record ShipmentDto(
    Guid Id,
    Guid VendorOrderId,
    Guid OrderId,
    Guid VendorId,
    string Carrier,
    string TrackingNumber,
    ShipmentStatus Status,
    DateTime? ShippedAtUtc,
    DateTime? EstimatedDeliveryUtc,
    DateTime? ActualDeliveryUtc,
    IReadOnlyList<TrackingEventDto> TrackingHistory,
    DateTime CreatedAtUtc);
