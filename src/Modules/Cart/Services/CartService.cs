using Microsoft.EntityFrameworkCore;
using MultiVendorPlatform.Infrastructure.Data;
using MultiVendorPlatform.Modules.Cart.Domain;
using MultiVendorPlatform.Modules.Cart.DTOs;
using MultiVendorPlatform.Modules.Common.Results;
using CartEntity = MultiVendorPlatform.Modules.Cart.Domain.Cart;

namespace MultiVendorPlatform.Modules.Cart.Services;

public interface ICartService
{
    Task<Result<CartDto>> GetCartAsync(Guid customerId, CancellationToken ct = default);
    Task<Result<CartDto>> AddItemAsync(Guid customerId, AddCartItemRequest request, CancellationToken ct = default);
    Task<Result<CartDto>> UpdateItemQuantityAsync(Guid customerId, Guid cartItemId, int quantity, CancellationToken ct = default);
    Task<Result<CartDto>> RemoveItemAsync(Guid customerId, Guid cartItemId, CancellationToken ct = default);
    Task<Result> ClearCartAsync(Guid customerId, CancellationToken ct = default);
}

public class CartService : ICartService
{
    private readonly ApplicationDbContext _dbContext;

    public CartService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CartDto>> GetCartAsync(Guid customerId, CancellationToken ct = default)
    {
        var cart = await GetOrCreateCartAsync(customerId, ct);
        return Result.Success(await MapToDtoAsync(cart, ct));
    }

    public async Task<Result<CartDto>> AddItemAsync(Guid customerId, AddCartItemRequest request, CancellationToken ct = default)
    {
        if (request.Quantity <= 0)
        {
            return Result.Failure<CartDto>(Error.Validation("Cart.InvalidQuantity", "Quantity must be greater than zero."));
        }

        var product = await _dbContext.Products
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == request.ProductId, ct);

        if (product == null || !product.IsActive)
        {
            return Result.Failure<CartDto>(Error.NotFound("Product.Unavailable", "Product is not available for purchase."));
        }

        decimal price = product.BasePrice;
        if (request.ProductVariantId.HasValue)
        {
            var variant = product.Variants.FirstOrDefault(v => v.Id == request.ProductVariantId.Value);
            if (variant == null || !variant.IsActive)
            {
                return Result.Failure<CartDto>(Error.NotFound("Variant.Unavailable", "Product variant is not available."));
            }
            price = variant.Price;
        }

        // Verify available stock
        var inventory = await _dbContext.Inventories
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.ProductId == request.ProductId && i.ProductVariantId == request.ProductVariantId, ct);

        if (inventory == null || inventory.AvailableQuantity < request.Quantity)
        {
            return Result.Failure<CartDto>(Error.Conflict("Cart.InsufficientStock",
                $"Only {inventory?.AvailableQuantity ?? 0} units are currently available in stock."));
        }

        var cart = await GetOrCreateCartAsync(customerId, ct);

        var existingItem = cart.Items.FirstOrDefault(i =>
            i.ProductId == request.ProductId && i.ProductVariantId == request.ProductVariantId);

        if (existingItem != null)
        {
            var totalQty = existingItem.Quantity + request.Quantity;
            if (inventory.AvailableQuantity < totalQty)
            {
                return Result.Failure<CartDto>(Error.Conflict("Cart.InsufficientStock",
                    $"Adding {request.Quantity} units would exceed available stock of {inventory.AvailableQuantity}."));
            }
            existingItem.Quantity = totalQty;
            existingItem.UnitPrice = price;
            existingItem.UpdatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            var item = new CartItem
            {
                CartId = cart.Id,
                ProductId = request.ProductId,
                ProductVariantId = request.ProductVariantId,
                Quantity = request.Quantity,
                UnitPrice = price
            };
            _dbContext.CartItems.Add(item);
        }

        cart.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(ct);

        var freshCart = await _dbContext.Carts
            .AsNoTracking()
            .Include(c => c.Items)
            .ThenInclude(i => i.Product)
            .ThenInclude(p => p.Vendor)
            .Include(c => c.Items)
            .ThenInclude(i => i.ProductVariant)
            .FirstAsync(c => c.Id == cart.Id, ct);

        return Result.Success(await MapToDtoAsync(freshCart, ct));
    }

    public async Task<Result<CartDto>> UpdateItemQuantityAsync(Guid customerId, Guid cartItemId, int quantity, CancellationToken ct = default)
    {
        var cart = await GetOrCreateCartAsync(customerId, ct);
        var item = cart.Items.FirstOrDefault(i => i.Id == cartItemId);
        if (item == null)
        {
            return Result.Failure<CartDto>(Error.NotFound("CartItem.NotFound", "Item not found in cart."));
        }

        if (quantity <= 0)
        {
            _dbContext.CartItems.Remove(item);
        }
        else
        {
            var inventory = await _dbContext.Inventories
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.ProductId == item.ProductId && i.ProductVariantId == item.ProductVariantId, ct);

            if (inventory == null || inventory.AvailableQuantity < quantity)
            {
                return Result.Failure<CartDto>(Error.Conflict("Cart.InsufficientStock",
                    $"Requested quantity {quantity} exceeds available stock of {inventory?.AvailableQuantity ?? 0}."));
            }

            item.Quantity = quantity;
            item.UpdatedAtUtc = DateTime.UtcNow;
        }

        cart.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(ct);

        var freshCart = await _dbContext.Carts
            .AsNoTracking()
            .Include(c => c.Items)
            .ThenInclude(i => i.Product)
            .ThenInclude(p => p.Vendor)
            .Include(c => c.Items)
            .ThenInclude(i => i.ProductVariant)
            .FirstAsync(c => c.Id == cart.Id, ct);

        return Result.Success(await MapToDtoAsync(freshCart, ct));
    }

    public async Task<Result<CartDto>> RemoveItemAsync(Guid customerId, Guid cartItemId, CancellationToken ct = default)
    {
        var cart = await GetOrCreateCartAsync(customerId, ct);
        var item = cart.Items.FirstOrDefault(i => i.Id == cartItemId);
        if (item == null)
        {
            return Result.Failure<CartDto>(Error.NotFound("CartItem.NotFound", "Item not found in cart."));
        }

        _dbContext.CartItems.Remove(item);
        cart.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(ct);

        var freshCart = await _dbContext.Carts
            .AsNoTracking()
            .Include(c => c.Items)
            .ThenInclude(i => i.Product)
            .ThenInclude(p => p.Vendor)
            .Include(c => c.Items)
            .ThenInclude(i => i.ProductVariant)
            .FirstAsync(c => c.Id == cart.Id, ct);

        return Result.Success(await MapToDtoAsync(freshCart, ct));
    }

    public async Task<Result> ClearCartAsync(Guid customerId, CancellationToken ct = default)
    {
        var cart = await _dbContext.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.CustomerId == customerId, ct);

        if (cart != null && cart.Items.Any())
        {
            _dbContext.CartItems.RemoveRange(cart.Items);
            await _dbContext.SaveChangesAsync(ct);
        }

        return Result.Success();
    }

    private async Task<CartEntity> GetOrCreateCartAsync(Guid customerId, CancellationToken ct)
    {
        var cart = await _dbContext.Carts
            .Include(c => c.Items)
            .ThenInclude(i => i.Product)
            .ThenInclude(p => p.Vendor)
            .Include(c => c.Items)
            .ThenInclude(i => i.ProductVariant)
            .FirstOrDefaultAsync(c => c.CustomerId == customerId, ct);

        if (cart == null)
        {
            cart = new CartEntity { CustomerId = customerId };
            _dbContext.Carts.Add(cart);
            await _dbContext.SaveChangesAsync(ct);
        }

        return cart;
    }

    private async Task<CartDto> MapToDtoAsync(CartEntity cart, CancellationToken ct)
    {
        var productIds = cart.Items.Select(i => i.ProductId).ToList();
        var inventories = await _dbContext.Inventories
            .AsNoTracking()
            .Where(inv => productIds.Contains(inv.ProductId))
            .ToListAsync(ct);

        var itemDtos = new List<CartItemDto>();
        bool hasStockIssues = false;

        foreach (var item in cart.Items)
        {
            var inv = inventories.FirstOrDefault(x => x.ProductId == item.ProductId && x.ProductVariantId == item.ProductVariantId);
            var available = inv?.AvailableQuantity ?? 0;
            var isAvailable = item.Product.IsActive && available >= item.Quantity;
            if (!isAvailable) hasStockIssues = true;

            var totalPrice = item.Quantity * item.UnitPrice;

            itemDtos.Add(new CartItemDto(
                item.Id,
                item.ProductId,
                item.Product.Name,
                item.Product.Sku,
                item.ProductVariantId,
                item.ProductVariant?.Name,
                item.Product.VendorId,
                item.Product.Vendor.BusinessName,
                item.Quantity,
                item.UnitPrice,
                totalPrice,
                isAvailable,
                available));
        }

        var subtotal = itemDtos.Sum(i => i.TotalPrice);
        var totalItems = itemDtos.Sum(i => i.Quantity);

        return new CartDto(cart.Id, cart.CustomerId, itemDtos, subtotal, totalItems, hasStockIssues);
    }
}
