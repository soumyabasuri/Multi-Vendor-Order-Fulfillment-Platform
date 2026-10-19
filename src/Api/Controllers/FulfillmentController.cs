using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiVendorPlatform.Modules.Fulfillment.DTOs;
using MultiVendorPlatform.Modules.Fulfillment.Services;
using MultiVendorPlatform.Modules.Identity.Domain;

namespace MultiVendorPlatform.Api.Controllers;

[Authorize]
public class FulfillmentController : BaseApiController
{
    private readonly IFulfillmentService _fulfillmentService;

    public FulfillmentController(IFulfillmentService fulfillmentService)
    {
        _fulfillmentService = fulfillmentService;
    }

    [HttpPost("shipments")]
    [Authorize(Roles = Role.Vendor)]
    [ProducesResponseType(typeof(ShipmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateShipment([FromBody] CreateShipmentRequest request, CancellationToken ct)
    {
        var vendorIdClaim = User.FindFirstValue("vendorId");
        if (!Guid.TryParse(vendorIdClaim, out var vendorId))
        {
            return Forbid("User is not associated with an approved vendor.");
        }

        var result = await _fulfillmentService.CreateShipmentAsync(vendorId, request, ct);
        return HandleResult(result);
    }

    [HttpPut("shipments/{id:guid}/status")]
    [Authorize(Roles = $"{Role.Vendor},{Role.Warehouse},{Role.Admin}")]
    [ProducesResponseType(typeof(ShipmentDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateShipmentStatus(Guid id, [FromBody] UpdateShipmentStatusRequest request, CancellationToken ct)
    {
        var vendorIdClaim = User.FindFirstValue("vendorId");
        Guid.TryParse(vendorIdClaim, out var vendorId);

        var result = await _fulfillmentService.UpdateShipmentStatusAsync(id, vendorId, request, ct);
        return HandleResult(result);
    }

    [HttpGet("shipments/{id:guid}")]
    [ProducesResponseType(typeof(ShipmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetShipment(Guid id, CancellationToken ct)
    {
        var result = await _fulfillmentService.GetShipmentByIdAsync(id, ct);
        return HandleResult(result);
    }

    [HttpGet("vendor-orders/{vendorOrderId:guid}/shipment")]
    [ProducesResponseType(typeof(ShipmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetShipmentByVendorOrder(Guid vendorOrderId, CancellationToken ct)
    {
        var result = await _fulfillmentService.GetShipmentByVendorOrderIdAsync(vendorOrderId, ct);
        return HandleResult(result);
    }
}
