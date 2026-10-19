using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiVendorPlatform.Modules.Catalog.DTOs;
using MultiVendorPlatform.Modules.Catalog.Services;
using MultiVendorPlatform.Modules.Identity.Domain;

namespace MultiVendorPlatform.Api.Controllers;

public class CategoriesController : BaseApiController
{
    private readonly ICatalogService _catalogService;

    public CategoriesController(ICatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<CategoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCategories(CancellationToken ct)
    {
        var result = await _catalogService.GetCategoriesAsync(ct);
        return HandleResult(result);
    }

    [HttpPost]
    [Authorize(Roles = Role.Admin)]
    [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryRequest request, CancellationToken ct)
    {
        var result = await _catalogService.CreateCategoryAsync(request, ct);
        return HandleResult(result);
    }
}
