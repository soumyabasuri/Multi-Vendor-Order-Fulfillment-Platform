using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MultiVendorPlatform.Infrastructure.Caching;
using MultiVendorPlatform.Infrastructure.Data;
using MultiVendorPlatform.Modules.Catalog.Domain;
using MultiVendorPlatform.Modules.Catalog.DTOs;
using MultiVendorPlatform.Modules.Common.Models;
using MultiVendorPlatform.Modules.Common.Results;
using MultiVendorPlatform.Modules.Inventory.Domain;
using MultiVendorPlatform.Modules.Vendors.Domain;

namespace MultiVendorPlatform.Modules.Catalog.Services;

public interface ICatalogService
{
    Task<Result<CategoryDto>> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CategoryDto>>> GetCategoriesAsync(CancellationToken ct = default);
    Task<Result<ProductDto>> CreateProductAsync(Guid vendorId, CreateProductRequest request, CancellationToken ct = default);
    Task<Result<ProductDto>> UpdateProductAsync(Guid productId, Guid vendorId, UpdateProductRequest request, CancellationToken ct = default);
    Task<Result<ProductDto>> GetProductByIdAsync(Guid productId, CancellationToken ct = default);
    Task<Result<PagedResult<ProductDto>>> GetProductsAsync(ProductFilterParams filter, CancellationToken ct = default);
    Task<Result<ProductVariantDto>> AddVariantAsync(Guid productId, Guid vendorId, CreateVariantRequest request, CancellationToken ct = default);
}

public class CatalogService : ICatalogService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICacheService _cacheService;

    public CatalogService(ApplicationDbContext dbContext, ICacheService cacheService)
    {
        _dbContext = dbContext;
        _cacheService = cacheService;
    }

    public async Task<Result<CategoryDto>> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken ct = default)
    {
        var slug = GenerateSlug(request.Name);
        var exists = await _dbContext.Categories.AnyAsync(c => c.Slug == slug, ct);
        if (exists)
        {
            slug = $"{slug}-{Guid.NewGuid().ToString()[..6]}";
        }

        var category = new Category
        {
            Name = request.Name,
            Slug = slug,
            Description = request.Description,
            ParentCategoryId = request.ParentCategoryId
        };

        _dbContext.Categories.Add(category);
        await _dbContext.SaveChangesAsync(ct);

        await _cacheService.RemoveAsync(CacheKeys.CategoryListKey, ct);
        return Result.Success(new CategoryDto(category.Id, category.Name, category.Slug, category.Description, category.ParentCategoryId));
    }

    public async Task<Result<IReadOnlyList<CategoryDto>>> GetCategoriesAsync(CancellationToken ct = default)
    {
        var cached = await _cacheService.GetAsync<List<CategoryDto>>(CacheKeys.CategoryListKey, ct);
        if (cached != null)
        {
            return Result.Success<IReadOnlyList<CategoryDto>>(cached);
        }

        var categories = await _dbContext.Categories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Slug, c.Description, c.ParentCategoryId))
            .ToListAsync(ct);

        await _cacheService.SetAsync(CacheKeys.CategoryListKey, categories, TimeSpan.FromMinutes(60), ct);
        return Result.Success<IReadOnlyList<CategoryDto>>(categories);
    }

    public async Task<Result<ProductDto>> CreateProductAsync(Guid vendorId, CreateProductRequest request, CancellationToken ct = default)
    {
        var vendor = await _dbContext.Vendors.FirstOrDefaultAsync(v => v.Id == vendorId, ct);
        if (vendor == null)
        {
            return Result.Failure<ProductDto>(Error.NotFound("Vendor.NotFound", "Vendor not found."));
        }

        if (vendor.Status != VendorStatus.Approved)
        {
            return Result.Failure<ProductDto>(Error.Forbidden("Vendor.NotApproved", "Vendor is not approved to sell products."));
        }

        var categoryExists = await _dbContext.Categories.AnyAsync(c => c.Id == request.CategoryId, ct);
        if (!categoryExists)
        {
            return Result.Failure<ProductDto>(Error.NotFound("Category.NotFound", "Category does not exist."));
        }

        var skuExists = await _dbContext.Products.AnyAsync(p => p.VendorId == vendorId && p.Sku == request.Sku, ct);
        if (skuExists)
        {
            return Result.Failure<ProductDto>(Error.Conflict("Product.SkuExists", "A product with this SKU already exists for this vendor."));
        }

        var slug = GenerateSlug(request.Name);
        var slugExists = await _dbContext.Products.AnyAsync(p => p.Slug == slug, ct);
        if (slugExists)
        {
            slug = $"{slug}-{Guid.NewGuid().ToString()[..6]}";
        }

        var product = new Product
        {
            VendorId = vendorId,
            CategoryId = request.CategoryId,
            Name = request.Name,
            Slug = slug,
            Description = request.Description,
            Sku = request.Sku,
            BasePrice = request.BasePrice,
            IsActive = true
        };

        // Create base inventory
        var inventory = new MultiVendorPlatform.Modules.Inventory.Domain.Inventory
        {
            ProductId = product.Id,
            ProductVariantId = null,
            Quantity = Math.Max(0, request.InitialStock),
            ReservedQuantity = 0
        };

        _dbContext.Products.Add(product);
        _dbContext.Inventories.Add(inventory);

        // Add variants if any
        if (request.Variants != null && request.Variants.Any())
        {
            foreach (var vReq in request.Variants)
            {
                var variant = new ProductVariant
                {
                    ProductId = product.Id,
                    Name = vReq.Name,
                    Sku = vReq.Sku,
                    Price = vReq.Price,
                    AttributesJson = vReq.AttributesJson,
                    IsActive = true
                };

                var variantInventory = new MultiVendorPlatform.Modules.Inventory.Domain.Inventory
                {
                    ProductId = product.Id,
                    ProductVariantId = variant.Id,
                    Quantity = Math.Max(0, vReq.InitialStock),
                    ReservedQuantity = 0
                };

                _dbContext.ProductVariants.Add(variant);
                _dbContext.Inventories.Add(variantInventory);
            }
        }

        await _dbContext.SaveChangesAsync(ct);
        await _cacheService.RemoveByPrefixAsync(CacheKeys.ProductListPrefix, ct);

        return await GetProductByIdAsync(product.Id, ct);
    }

    public async Task<Result<ProductDto>> UpdateProductAsync(Guid productId, Guid vendorId, UpdateProductRequest request, CancellationToken ct = default)
    {
        var product = await _dbContext.Products.FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product == null)
        {
            return Result.Failure<ProductDto>(Error.NotFound("Product.NotFound", "Product not found."));
        }

        if (product.VendorId != vendorId)
        {
            return Result.Failure<ProductDto>(Error.Forbidden("Product.NotAuthorized", "You do not have permission to modify this product."));
        }

        product.Name = request.Name;
        product.Description = request.Description;
        product.BasePrice = request.BasePrice;
        product.IsActive = request.IsActive;
        product.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(ct);

        // Invalidate cache
        await _cacheService.RemoveAsync(CacheKeys.ProductById(productId), ct);
        await _cacheService.RemoveByPrefixAsync(CacheKeys.ProductListPrefix, ct);

        return await GetProductByIdAsync(productId, ct);
    }

    public async Task<Result<ProductDto>> GetProductByIdAsync(Guid productId, CancellationToken ct = default)
    {
        var cacheKey = CacheKeys.ProductById(productId);
        var cached = await _cacheService.GetAsync<ProductDto>(cacheKey, ct);
        if (cached != null)
        {
            return Result.Success(cached);
        }

        var product = await _dbContext.Products
            .AsNoTracking()
            .Include(p => p.Vendor)
            .Include(p => p.Category)
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == productId, ct);

        if (product == null)
        {
            return Result.Failure<ProductDto>(Error.NotFound("Product.NotFound", "Product not found."));
        }

        var inventories = await _dbContext.Inventories
            .AsNoTracking()
            .Where(i => i.ProductId == productId)
            .ToListAsync(ct);

        var baseInv = inventories.FirstOrDefault(i => i.ProductVariantId == null);
        var baseAvailable = baseInv?.AvailableQuantity ?? 0;

        var variantDtos = product.Variants.Select(v =>
        {
            var varInv = inventories.FirstOrDefault(i => i.ProductVariantId == v.Id);
            return new ProductVariantDto(
                v.Id,
                v.ProductId,
                v.Name,
                v.Sku,
                v.Price,
                v.AttributesJson,
                v.IsActive,
                varInv?.AvailableQuantity ?? 0);
        }).ToList();

        var dto = new ProductDto(
            product.Id,
            product.VendorId,
            product.Vendor.BusinessName,
            product.CategoryId,
            product.Category.Name,
            product.Name,
            product.Slug,
            product.Description,
            product.Sku,
            product.BasePrice,
            product.IsActive,
            baseAvailable,
            variantDtos,
            product.CreatedAtUtc);

        await _cacheService.SetAsync(cacheKey, dto, TimeSpan.FromMinutes(15), ct);
        return Result.Success(dto);
    }

    public async Task<Result<PagedResult<ProductDto>>> GetProductsAsync(ProductFilterParams filter, CancellationToken ct = default)
    {
        var query = _dbContext.Products
            .AsNoTracking()
            .Include(p => p.Vendor)
            .Include(p => p.Category)
            .Include(p => p.Variants)
            .AsQueryable();

        if (filter.IsActive.HasValue)
        {
            query = query.Where(p => p.IsActive == filter.IsActive.Value);
        }

        if (filter.CategoryId.HasValue)
        {
            query = query.Where(p => p.CategoryId == filter.CategoryId.Value);
        }

        if (filter.VendorId.HasValue)
        {
            query = query.Where(p => p.VendorId == filter.VendorId.Value);
        }

        if (filter.MinPrice.HasValue)
        {
            query = query.Where(p => p.BasePrice >= filter.MinPrice.Value);
        }

        if (filter.MaxPrice.HasValue)
        {
            query = query.Where(p => p.BasePrice <= filter.MaxPrice.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToLower();
            query = query.Where(p =>
                EF.Functions.ILike(p.Name, $"%{search}%") ||
                EF.Functions.ILike(p.Description, $"%{search}%") ||
                EF.Functions.ILike(p.Sku, $"%{search}%"));
        }

        query = filter.SortBy switch
        {
            "price_asc" => query.OrderBy(p => p.BasePrice),
            "price_desc" => query.OrderByDescending(p => p.BasePrice),
            "name" => query.OrderBy(p => p.Name),
            _ => query.OrderByDescending(p => p.CreatedAtUtc)
        };

        var totalCount = await query.CountAsync(ct);
        var products = await query
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        var productIds = products.Select(p => p.Id).ToList();
        var inventories = await _dbContext.Inventories
            .AsNoTracking()
            .Where(i => productIds.Contains(i.ProductId))
            .ToListAsync(ct);

        var items = products.Select(p =>
        {
            var baseInv = inventories.FirstOrDefault(i => i.ProductId == p.Id && i.ProductVariantId == null);
            var variantDtos = p.Variants.Select(v =>
            {
                var vInv = inventories.FirstOrDefault(i => i.ProductVariantId == v.Id);
                return new ProductVariantDto(
                    v.Id,
                    v.ProductId,
                    v.Name,
                    v.Sku,
                    v.Price,
                    v.AttributesJson,
                    v.IsActive,
                    vInv?.AvailableQuantity ?? 0);
            }).ToList();

            return new ProductDto(
                p.Id,
                p.VendorId,
                p.Vendor.BusinessName,
                p.CategoryId,
                p.Category.Name,
                p.Name,
                p.Slug,
                p.Description,
                p.Sku,
                p.BasePrice,
                p.IsActive,
                baseInv?.AvailableQuantity ?? 0,
                variantDtos,
                p.CreatedAtUtc);
        }).ToList();

        return Result.Success(new PagedResult<ProductDto>(items, totalCount, filter.PageNumber, filter.PageSize));
    }

    public async Task<Result<ProductVariantDto>> AddVariantAsync(Guid productId, Guid vendorId, CreateVariantRequest request, CancellationToken ct = default)
    {
        var product = await _dbContext.Products.FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product == null)
        {
            return Result.Failure<ProductVariantDto>(Error.NotFound("Product.NotFound", "Product not found."));
        }

        if (product.VendorId != vendorId)
        {
            return Result.Failure<ProductVariantDto>(Error.Forbidden("Product.NotAuthorized", "You do not have permission to add variants to this product."));
        }

        var skuExists = await _dbContext.ProductVariants.AnyAsync(pv => pv.ProductId == productId && pv.Sku == request.Sku, ct);
        if (skuExists)
        {
            return Result.Failure<ProductVariantDto>(Error.Conflict("Variant.SkuExists", "A variant with this SKU already exists for this product."));
        }

        var variant = new ProductVariant
        {
            ProductId = productId,
            Name = request.Name,
            Sku = request.Sku,
            Price = request.Price,
            AttributesJson = request.AttributesJson,
            IsActive = true
        };

        var inventory = new MultiVendorPlatform.Modules.Inventory.Domain.Inventory
        {
            ProductId = productId,
            ProductVariantId = variant.Id,
            Quantity = Math.Max(0, request.InitialStock),
            ReservedQuantity = 0
        };

        _dbContext.ProductVariants.Add(variant);
        _dbContext.Inventories.Add(inventory);
        await _dbContext.SaveChangesAsync(ct);

        await _cacheService.RemoveAsync(CacheKeys.ProductById(productId), ct);
        await _cacheService.RemoveByPrefixAsync(CacheKeys.ProductListPrefix, ct);

        return Result.Success(new ProductVariantDto(
            variant.Id,
            variant.ProductId,
            variant.Name,
            variant.Sku,
            variant.Price,
            variant.AttributesJson,
            variant.IsActive,
            inventory.AvailableQuantity));
    }

    private static string GenerateSlug(string text)
    {
        var str = text.ToLowerInvariant();
        str = Regex.Replace(str, @"[^a-z0-9\s-]", "");
        str = Regex.Replace(str, @"\s+", " ").Trim();
        str = Regex.Replace(str, @"\s", "-");
        return str;
    }
}
