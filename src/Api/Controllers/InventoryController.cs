using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiVendorPlatform.Modules.Identity.Domain;
using MultiVendorPlatform.Modules.Inventory.DTOs;
using MultiVendorPlatform.Modules.Inventory.Services;

namespace MultiVendorPlatform.Api.Controllers;

public class InventoryController : BaseApiController
{
    private readonly IInventoryService _inventoryService;

    public InventoryController(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    [HttpGet("{productId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(InventoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInventory(Guid productId, [FromQuery] Guid? variantId, CancellationToken ct)
    {
        var result = await _inventoryService.GetInventoryAsync(productId, variantId, ct);
        return HandleResult(result);
    }

    [HttpPost("adjust")]
    [Authorize(Roles = $"{Role.Vendor},{Role.Warehouse},{Role.Admin}")]
    [ProducesResponseType(typeof(InventoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AdjustStock([FromBody] AdjustStockRequest request, CancellationToken ct)
    {
        var result = await _inventoryService.AdjustStockAsync(request, ct);
        return HandleResult(result);
    }
}
