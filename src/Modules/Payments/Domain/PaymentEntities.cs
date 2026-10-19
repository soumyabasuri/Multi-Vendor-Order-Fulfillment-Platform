using MultiVendorPlatform.Modules.Common.Domain;
using MultiVendorPlatform.Modules.Orders.Domain;

namespace MultiVendorPlatform.Modules.Payments.Domain;

public enum PaymentStatus
{
    Pending = 0,
    Processing = 1,
    Succeeded = 2,
    Failed = 3,
    Refunded = 4
}

public class Payment : AggregateRoot<Guid>
{
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string PaymentMethod { get; set; } = "CreditCard";
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    public string? TransactionReference { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? ProviderResponseJson { get; set; }
    public string? FailureReason { get; set; }

    public Payment()
    {
        Id = Guid.NewGuid();
    }
}
