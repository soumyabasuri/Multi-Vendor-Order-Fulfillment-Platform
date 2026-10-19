using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiVendorPlatform.Modules.Cart.DTOs;
using MultiVendorPlatform.Modules.Cart.Services;
using MultiVendorPlatform.Modules.Identity.Domain;

namespace MultiVendorPlatform.Api.Controllers;

[Authorize]
public class CartController : BaseApiController
{
    private readonly ICartService _cartService;

    public CartController(ICartService cartService)
    {
        _cartService = cartService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCart(CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var customerId)) return Unauthorized();

        var result = await _cartService.GetCartAsync(customerId, ct);
        return HandleResult(result);
    }

    [HttpPost("items")]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddItem([FromBody] AddCartItemRequest request, CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var customerId)) return Unauthorized();

        var result = await _cartService.AddItemAsync(customerId, request, ct);
        return HandleResult(result);
    }

    [HttpPut("items/{itemId:guid}")]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateItemQuantity(Guid itemId, [FromBody] UpdateCartItemRequest request, CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var customerId)) return Unauthorized();

        var result = await _cartService.UpdateItemQuantityAsync(customerId, itemId, request.Quantity, ct);
        return HandleResult(result);
    }

    [HttpDelete("items/{itemId:guid}")]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveItem(Guid itemId, CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var customerId)) return Unauthorized();

        var result = await _cartService.RemoveItemAsync(customerId, itemId, ct);
        return HandleResult(result);
    }

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ClearCart(CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var customerId)) return Unauthorized();

        var result = await _cartService.ClearCartAsync(customerId, ct);
        return HandleResult(result);
    }
}
