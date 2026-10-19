using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MultiVendorPlatform.Infrastructure.Data;
using MultiVendorPlatform.Infrastructure.Kafka;
using MultiVendorPlatform.Infrastructure.Outbox;
using MultiVendorPlatform.Modules.Common.Results;
using MultiVendorPlatform.Modules.Orders.Domain;
using MultiVendorPlatform.Modules.Payments.Domain;
using MultiVendorPlatform.Modules.Payments.DTOs;

namespace MultiVendorPlatform.Modules.Payments.Services;

public interface IPaymentService
{
    Task<Result<PaymentDto>> InitiatePaymentAsync(InitiatePaymentRequest request, CancellationToken ct = default);
    Task<Result<PaymentDto>> GetPaymentByIdAsync(Guid paymentId, CancellationToken ct = default);
    Task<Result<PaymentDto>> GetPaymentByOrderIdAsync(Guid orderId, CancellationToken ct = default);
}

public class PaymentService : IPaymentService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IOutboxService _outboxService;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        ApplicationDbContext dbContext,
        IOutboxService outboxService,
        ILogger<PaymentService> logger)
    {
        _dbContext = dbContext;
        _outboxService = outboxService;
        _logger = logger;
    }

    public async Task<Result<PaymentDto>> InitiatePaymentAsync(InitiatePaymentRequest request, CancellationToken ct = default)
    {
        // 1. Idempotency check: if already initiated with this key, return existing payment
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existingPayment = await _dbContext.Payments
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.IdempotencyKey == request.IdempotencyKey, ct);

            if (existingPayment != null)
            {
                _logger.LogInformation("Idempotent payment match for Key: {Key}. Returning existing payment {PaymentId}",
                    request.IdempotencyKey, existingPayment.Id);
                return Result.Success(MapToDto(existingPayment));
            }
        }

        var order = await _dbContext.Orders.FirstOrDefaultAsync(o => o.Id == request.OrderId, ct);
        if (order == null)
        {
            return Result.Failure<PaymentDto>(Error.NotFound("Order.NotFound", "Order not found."));
        }

        if (order.Status is not (OrderStatus.InventoryReserved or OrderStatus.PaymentPending))
        {
            return Result.Failure<PaymentDto>(Error.Conflict("Payment.InvalidOrderStatus",
                $"Cannot process payment for order in status '{order.Status}'."));
        }

        await using var tx = await _dbContext.Database.BeginTransactionAsync(ct);
        try
        {
            order.Status = OrderStatus.PaymentPending;
            order.UpdatedAtUtc = DateTime.UtcNow;

            var payment = new Payment
            {
                OrderId = order.Id,
                Amount = order.TotalAmount,
                Currency = "USD",
                PaymentMethod = request.PaymentMethod,
                IdempotencyKey = request.IdempotencyKey,
                Status = PaymentStatus.Processing,
                CreatedAtUtc = DateTime.UtcNow
            };

            _dbContext.Payments.Add(payment);
            await _dbContext.SaveChangesAsync(ct);

            // 2. Enqueue PaymentInitiated
            var initiatedEvent = new PaymentInitiatedEvent(payment.Id, order.Id, payment.Amount, payment.Currency, DateTime.UtcNow);
            _outboxService.Enqueue(initiatedEvent, KafkaTopics.PaymentInitiated, nameof(PaymentInitiatedEvent), "Payment", payment.Id.ToString());

            // 3. Simulate Gateway Execution
            // Rule: Card ending with "0000" simulates decline/failure; others succeed
            bool isSuccess = !(request.CardNumber?.EndsWith("0000") ?? false);

            if (isSuccess)
            {
                payment.Status = PaymentStatus.Succeeded;
                payment.TransactionReference = $"TXN-{Guid.NewGuid().ToString()[..12].ToUpperInvariant()}";
                payment.ProviderResponseJson = JsonSerializer.Serialize(new { status = "approved", authCode = "AUTH99" });
                payment.UpdatedAtUtc = DateTime.UtcNow;

                var completedEvent = new PaymentCompletedEvent(
                    payment.Id,
                    order.Id,
                    payment.TransactionReference,
                    payment.Amount,
                    DateTime.UtcNow);

                _outboxService.Enqueue(completedEvent, KafkaTopics.PaymentCompleted, nameof(PaymentCompletedEvent), "Payment", payment.Id.ToString());
                _logger.LogInformation("Simulated payment succeeded for Order {OrderId}. Ref: {Ref}", order.Id, payment.TransactionReference);
            }
            else
            {
                payment.Status = PaymentStatus.Failed;
                payment.FailureReason = "Card declined by issuing bank (insufficient funds / test decline).";
                payment.ProviderResponseJson = JsonSerializer.Serialize(new { status = "declined", code = "do_not_honor" });
                payment.UpdatedAtUtc = DateTime.UtcNow;

                var failedEvent = new PaymentFailedEvent(
                    payment.Id,
                    order.Id,
                    payment.FailureReason,
                    DateTime.UtcNow);

                _outboxService.Enqueue(failedEvent, KafkaTopics.PaymentFailed, nameof(PaymentFailedEvent), "Payment", payment.Id.ToString());
                _logger.LogWarning("Simulated payment failed for Order {OrderId}. Reason: {Reason}", order.Id, payment.FailureReason);
            }

            await _dbContext.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Result.Success(MapToDto(payment));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception initiating payment for Order {OrderId}", request.OrderId);
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<Result<PaymentDto>> GetPaymentByIdAsync(Guid paymentId, CancellationToken ct = default)
    {
        var payment = await _dbContext.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paymentId, ct);
        return payment == null
            ? Result.Failure<PaymentDto>(Error.NotFound("Payment.NotFound", "Payment record not found."))
            : Result.Success(MapToDto(payment));
    }

    public async Task<Result<PaymentDto>> GetPaymentByOrderIdAsync(Guid orderId, CancellationToken ct = default)
    {
        var payment = await _dbContext.Payments.AsNoTracking()
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(p => p.OrderId == orderId, ct);

        return payment == null
            ? Result.Failure<PaymentDto>(Error.NotFound("Payment.NotFound", "No payment found for order."))
            : Result.Success(MapToDto(payment));
    }

    private static PaymentDto MapToDto(Payment p) => new(
        p.Id,
        p.OrderId,
        p.Amount,
        p.Currency,
        p.PaymentMethod,
        p.Status,
        p.TransactionReference,
        p.IdempotencyKey,
        p.FailureReason,
        p.CreatedAtUtc);
}
