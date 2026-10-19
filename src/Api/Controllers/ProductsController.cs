using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MultiVendorPlatform.Infrastructure.Data;
using MultiVendorPlatform.Modules.Catalog.DTOs;
using MultiVendorPlatform.Modules.Catalog.Services;
using MultiVendorPlatform.Modules.Common.Models;
using MultiVendorPlatform.Modules.Identity.Domain;
using MultiVendorPlatform.Modules.Vendors.Domain;

namespace MultiVendorPlatform.Api.Controllers;

public class ProductsController : BaseApiController
{
    private readonly ICatalogService _catalogService;
    private readonly ApplicationDbContext _dbContext;

    public ProductsController(ICatalogService catalogService, ApplicationDbContext dbContext)
    {
        _catalogService = catalogService;
        _dbContext = dbContext;
    }

    private async Task<Guid?> ResolveVendorIdAsync(CancellationToken ct)
    {
        var vendorIdClaim = User.FindFirstValue("vendorId");
        if (Guid.TryParse(vendorIdClaim, out var vId) && vId != Guid.Empty)
        {
            return vId;
        }

        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(userIdStr, out var userId))
        {
            var vendor = await _dbContext.Vendors.FirstOrDefaultAsync(v => v.OwnerUserId == userId, ct);
            if (vendor == null)
            {
                var user = await _dbContext.Users.FindAsync(new object[] { userId }, ct);
                if (user != null)
                {
                    vendor = new Vendor
                    {
                        Id = Guid.NewGuid(),
                        OwnerUserId = user.Id,
                        BusinessName = $"{user.FirstName}'s Store",
                        Slug = $"{user.FirstName.ToLowerInvariant()}-{Guid.NewGuid().ToString("N")[..6]}",
                        ContactEmail = user.Email,
                        Status = VendorStatus.Approved,
                        CommissionRate = 0.10m
                    };
                    _dbContext.Vendors.Add(vendor);
                    user.VendorId = vendor.Id;
                    await _dbContext.SaveChangesAsync(ct);
                }
            }
            else if (vendor.Status != VendorStatus.Approved)
            {
                vendor.Status = VendorStatus.Approved;
                await _dbContext.SaveChangesAsync(ct);
            }

            if (vendor != null) return vendor.Id;
        }

        return null;
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
        var vendorId = await ResolveVendorIdAsync(ct);
        if (!vendorId.HasValue)
        {
            return Forbidden("User is not registered as an active vendor.");
        }

        var result = await _catalogService.CreateProductAsync(vendorId.Value, request, ct);
        return HandleResult(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = Role.Vendor)]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateProduct(Guid id, [FromBody] UpdateProductRequest request, CancellationToken ct)
    {
        var vendorId = await ResolveVendorIdAsync(ct);
        if (!vendorId.HasValue)
        {
            return Forbidden("User is not registered as an active vendor.");
        }

        var result = await _catalogService.UpdateProductAsync(id, vendorId.Value, request, ct);
        return HandleResult(result);
    }

    [HttpPost("{id:guid}/variants")]
    [Authorize(Roles = Role.Vendor)]
    [ProducesResponseType(typeof(ProductVariantDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddVariant(Guid id, [FromBody] CreateVariantRequest request, CancellationToken ct)
    {
        var vendorId = await ResolveVendorIdAsync(ct);
        if (!vendorId.HasValue)
        {
            return Forbidden("User is not registered as an active vendor.");
        }

        var result = await _catalogService.AddVariantAsync(id, vendorId.Value, request, ct);
        return HandleResult(result);
    }
}
