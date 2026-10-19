using MultiVendorPlatform.Modules.Payments.Domain;

namespace MultiVendorPlatform.Modules.Payments.DTOs;

public record InitiatePaymentRequest(
    Guid OrderId,
    string PaymentMethod,
    string? CardNumber,
    string? ExpirationMonth,
    string? ExpirationYear,
    string? Cvv,
    string? IdempotencyKey = null);

public record SimulatePaymentResultRequest(
    Guid PaymentId,
    bool ShouldSucceed,
    string? FailureReason = null);

public record PaymentDto(
    Guid Id,
    Guid OrderId,
    decimal Amount,
    string Currency,
    string PaymentMethod,
    PaymentStatus Status,
    string? TransactionReference,
    string? IdempotencyKey,
    string? FailureReason,
    DateTime CreatedAtUtc);
