using MultiVendorPlatform.Modules.Vendors.Domain;

namespace MultiVendorPlatform.Modules.Vendors.DTOs;

public record RegisterVendorRequest(
    string BusinessName,
    string Description,
    string ContactEmail,
    string? ContactPhone,
    decimal? CommissionRate = 0.10m);

public record UpdateVendorRequest(
    string BusinessName,
    string Description,
    string ContactEmail,
    string? ContactPhone);

public record ApproveVendorRequest(
    bool Approved,
    string? RejectionReason = null,
    decimal? CommissionRate = null);

public record VendorDto(
    Guid Id,
    Guid OwnerUserId,
    string BusinessName,
    string Slug,
    string Description,
    string ContactEmail,
    string? ContactPhone,
    VendorStatus Status,
    decimal CommissionRate,
    string? RejectionReason,
    DateTime? ApprovedAtUtc,
    DateTime CreatedAtUtc);
