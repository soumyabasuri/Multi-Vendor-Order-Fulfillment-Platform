using MultiVendorPlatform.Modules.Common.Models;

namespace MultiVendorPlatform.Modules.Catalog.DTOs;

public record CreateCategoryRequest(
    string Name,
    string? Description = null,
    Guid? ParentCategoryId = null);

public record CategoryDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    Guid? ParentCategoryId);

public record CreateVariantRequest(
    string Name,
    string Sku,
    decimal Price,
    string AttributesJson,
    int InitialStock);

public record CreateProductRequest(
    Guid CategoryId,
    string Name,
    string Description,
    string Sku,
    decimal BasePrice,
    int InitialStock = 0,
    List<CreateVariantRequest>? Variants = null);

public record UpdateProductRequest(
    string Name,
    string Description,
    decimal BasePrice,
    bool IsActive);

public record ProductVariantDto(
    Guid Id,
    Guid ProductId,
    string Name,
    string Sku,
    decimal Price,
    string AttributesJson,
    bool IsActive,
    int AvailableStock);

public record ProductDto(
    Guid Id,
    Guid VendorId,
    string VendorName,
    Guid CategoryId,
    string CategoryName,
    string Name,
    string Slug,
    string Description,
    string Sku,
    decimal BasePrice,
    bool IsActive,
    int AvailableStock,
    List<ProductVariantDto> Variants,
    DateTime CreatedAtUtc);

public record ProductFilterParams : PaginationFilter
{
    public string? Search { get; init; }
    public Guid? CategoryId { get; init; }
    public Guid? VendorId { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public string? SortBy { get; init; } // "price_asc", "price_desc", "name", "newest"
    public bool? IsActive { get; init; } = true;
}
