using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiVendorPlatform.Infrastructure.Idempotency;
using MultiVendorPlatform.Modules.Common.Models;
using MultiVendorPlatform.Modules.Identity.Domain;
using MultiVendorPlatform.Modules.Orders.DTOs;
using MultiVendorPlatform.Modules.Orders.Services;

namespace MultiVendorPlatform.Api.Controllers;

public class OrdersController : BaseApiController
{
    private readonly IOrderService _orderService;

    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    [HttpPost]
    [Idempotent]
    [Authorize(Roles = Role.Customer)]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request, CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var customerId)) return Unauthorized();

        var result = await _orderService.CreateOrderAsync(customerId, request, ct);
        return HandleResult(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrderById(Guid id, CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var role = User.FindFirstValue(ClaimTypes.Role) ?? Role.Customer;
        var result = await _orderService.GetOrderByIdAsync(id, userId, role, ct);
        return HandleResult(result);
    }

    [HttpGet("my-orders")]
    [Authorize(Roles = Role.Customer)]
    [ProducesResponseType(typeof(PagedResult<OrderSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyOrders([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var customerId)) return Unauthorized();

        var result = await _orderService.GetCustomerOrdersAsync(customerId, pageNumber, pageSize, ct);
        return HandleResult(result);
    }

    [HttpGet("vendor-orders")]
    [Authorize(Roles = Role.Vendor)]
    [ProducesResponseType(typeof(PagedResult<VendorOrderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVendorOrders([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var vendorIdClaim = User.FindFirstValue("vendorId");
        if (!Guid.TryParse(vendorIdClaim, out var vendorId))
        {
            return Forbidden("User is not associated with a vendor profile.");
        }

        var result = await _orderService.GetVendorOrdersAsync(vendorId, pageNumber, pageSize, ct);
        return HandleResult(result);
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelOrder(Guid id, [FromBody] CancelOrderRequest request, CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var result = await _orderService.CancelOrderAsync(id, userId, request.Reason, ct);
        return HandleResult(result);
    }
}
