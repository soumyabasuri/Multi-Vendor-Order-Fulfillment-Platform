using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiVendorPlatform.Modules.Catalog.DTOs;
using MultiVendorPlatform.Modules.Catalog.Services;
using MultiVendorPlatform.Modules.Common.Models;
using MultiVendorPlatform.Modules.Identity.Domain;

namespace MultiVendorPlatform.Api.Controllers;

public class ProductsController : BaseApiController
{
    private readonly ICatalogService _catalogService;

    public ProductsController(ICatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PagedResult<ProductDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProducts([FromQuery] ProductFilterParams filter, CancellationToken ct)
    {
        var result = await _catalogService.GetProductsAsync(filter, ct);
        return HandleResult(result);
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProductById(Guid id, CancellationToken ct)
    {
        var result = await _catalogService.GetProductByIdAsync(id, ct);
        return HandleResult(result);
    }

    [HttpPost]
    [Authorize(Roles = Role.Vendor)]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateProduct([FromBody] CreateProductRequest request, CancellationToken ct)
    {
        var vendorIdClaim = User.FindFirstValue("vendorId");
        if (!Guid.TryParse(vendorIdClaim, out var vendorId))
        {
            return Forbid("User is not registered as an active vendor.");
        }

        var result = await _catalogService.CreateProductAsync(vendorId, request, ct);
        return HandleResult(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = Role.Vendor)]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateProduct(Guid id, [FromBody] UpdateProductRequest request, CancellationToken ct)
    {
        var vendorIdClaim = User.FindFirstValue("vendorId");
        if (!Guid.TryParse(vendorIdClaim, out var vendorId))
        {
            return Forbid("User is not registered as an active vendor.");
        }

        var result = await _catalogService.UpdateProductAsync(id, vendorId, request, ct);
        return HandleResult(result);
    }

    [HttpPost("{id:guid}/variants")]
    [Authorize(Roles = Role.Vendor)]
    [ProducesResponseType(typeof(ProductVariantDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddVariant(Guid id, [FromBody] CreateVariantRequest request, CancellationToken ct)
    {
        var vendorIdClaim = User.FindFirstValue("vendorId");
        if (!Guid.TryParse(vendorIdClaim, out var vendorId))
        {
            return Forbid("User is not registered as an active vendor.");
        }

        var result = await _catalogService.AddVariantAsync(id, vendorId, request, ct);
        return HandleResult(result);
    }
}
