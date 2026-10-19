using MultiVendorPlatform.Modules.Common.Domain;
using MultiVendorPlatform.Modules.Identity.Domain;

namespace MultiVendorPlatform.Modules.Vendors.Domain;

public enum VendorStatus
{
    PendingApproval = 0,
    Approved = 1,
    Suspended = 2,
    Rejected = 3
}

public class Vendor : AggregateRoot<Guid>
{
    public Guid OwnerUserId { get; set; }
    public User OwnerUser { get; set; } = null!;

    public string BusinessName { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string? ContactPhone { get; set; }
    public VendorStatus Status { get; set; } = VendorStatus.PendingApproval;
    public decimal CommissionRate { get; set; } = 0.10m; // Default 10% platform fee
    public string? RejectionReason { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }

    public Vendor()
    {
        Id = Guid.NewGuid();
    }
}
