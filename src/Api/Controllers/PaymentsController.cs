using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiVendorPlatform.Infrastructure.Idempotency;
using MultiVendorPlatform.Modules.Payments.DTOs;
using MultiVendorPlatform.Modules.Payments.Services;

namespace MultiVendorPlatform.Api.Controllers;

[Authorize]
public class PaymentsController : BaseApiController
{
    private readonly IPaymentService _paymentService;

    public PaymentsController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpPost]
    [Idempotent]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> InitiatePayment([FromBody] InitiatePaymentRequest request, CancellationToken ct)
    {
        // Extract Idempotency-Key header if provided
        var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault();
        var enrichedRequest = request with { IdempotencyKey = idempotencyKey ?? request.IdempotencyKey };

        var result = await _paymentService.InitiatePaymentAsync(enrichedRequest, ct);
        return HandleResult(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPayment(Guid id, CancellationToken ct)
    {
        var result = await _paymentService.GetPaymentByIdAsync(id, ct);
        return HandleResult(result);
    }

    [HttpGet("order/{orderId:guid}")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPaymentByOrderId(Guid orderId, CancellationToken ct)
    {
        var result = await _paymentService.GetPaymentByOrderIdAsync(orderId, ct);
        return HandleResult(result);
    }
}
