using Microsoft.EntityFrameworkCore;
using MultiVendorPlatform.Infrastructure.Caching;
using MultiVendorPlatform.Infrastructure.Data;
using MultiVendorPlatform.Modules.Common.Results;
using MultiVendorPlatform.Modules.Inventory.Domain;
using MultiVendorPlatform.Modules.Inventory.DTOs;

namespace MultiVendorPlatform.Modules.Inventory.Services;

public interface IInventoryService
{
    Task<Result<InventoryDto>> GetInventoryAsync(Guid productId, Guid? variantId = null, CancellationToken ct = default);
    Task<Result<InventoryDto>> AdjustStockAsync(AdjustStockRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<InventoryReservation>>> ReserveInventoryAsync(Guid orderId, IEnumerable<ReserveStockItem> items, TimeSpan? reservationTtl = null, CancellationToken ct = default);
    Task<Result<IReadOnlyList<InventoryReservation>>> ReserveInventoryAtomicAsync(Guid orderId, IEnumerable<ReserveStockItem> items, TimeSpan? reservationTtl = null, CancellationToken ct = default);
    Task<Result> ReleaseReservationAsync(Guid orderId, CancellationToken ct = default);
    Task<Result> CommitReservationAsync(Guid orderId, CancellationToken ct = default);
}

public class InventoryService : IInventoryService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICacheService _cacheService;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(
        ApplicationDbContext dbContext,
        ICacheService cacheService,
        ILogger<InventoryService> logger)
    {
        _dbContext = dbContext;
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task<Result<InventoryDto>> GetInventoryAsync(Guid productId, Guid? variantId = null, CancellationToken ct = default)
    {
        var inventory = await _dbContext.Inventories
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.ProductId == productId && i.ProductVariantId == variantId, ct);

        if (inventory == null)
        {
            return Result.Failure<InventoryDto>(Error.NotFound("Inventory.NotFound", "Inventory record not found for product."));
        }

        return Result.Success(MapToDto(inventory));
    }

    public async Task<Result<InventoryDto>> AdjustStockAsync(AdjustStockRequest request, CancellationToken ct = default)
    {
        await using var tx = await _dbContext.Database.BeginTransactionAsync(ct);

        var inventory = await _dbContext.Inventories
            .FirstOrDefaultAsync(i => i.ProductId == request.ProductId && i.ProductVariantId == request.ProductVariantId, ct);

        if (inventory == null)
        {
            return Result.Failure<InventoryDto>(Error.NotFound("Inventory.NotFound", "Inventory record does not exist."));
        }

        var newQuantity = inventory.Quantity + request.QuantityAdjustment;
        if (newQuantity < inventory.ReservedQuantity)
        {
            return Result.Failure<InventoryDto>(Error.Conflict("Inventory.InvalidQuantity",
                $"Cannot adjust stock to {newQuantity} because {inventory.ReservedQuantity} units are currently reserved."));
        }

        if (newQuantity < 0)
        {
            return Result.Failure<InventoryDto>(Error.Conflict("Inventory.NegativeStock", "Physical inventory quantity cannot be negative."));
        }

        inventory.Quantity = newQuantity;
        inventory.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await _cacheService.RemoveAsync(CacheKeys.ProductById(request.ProductId), ct);

        return Result.Success(MapToDto(inventory));
    }

    public async Task<Result<IReadOnlyList<InventoryReservation>>> ReserveInventoryAsync(
        Guid orderId,
        IEnumerable<ReserveStockItem> items,
        TimeSpan? reservationTtl = null,
        CancellationToken ct = default)
    {
        var ttl = reservationTtl ?? TimeSpan.FromMinutes(15);
        var expiresAtUtc = DateTime.UtcNow.Add(ttl);
        var reservations = new List<InventoryReservation>();

        // Sort items by ProductId then VariantId to ensure consistent lock ordering across transactions and avoid deadlocks
        var sortedItems = items
            .OrderBy(i => i.ProductId)
            .ThenBy(i => i.ProductVariantId ?? Guid.Empty)
            .ToList();

        // Use PostgreSQL SELECT ... FOR UPDATE or atomic updates inside transaction
        await using var tx = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);

        try
        {
            foreach (var item in sortedItems)
            {
                if (item.Quantity <= 0)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<IReadOnlyList<InventoryReservation>>(Error.Validation("Inventory.InvalidQuantity", "Reserved quantity must be greater than zero."));
                }

                // Query with EF Core or raw SQL for pessimistic row-level lock
                var inventory = await _dbContext.Inventories
                    .FromSqlInterpolated($"SELECT * FROM inventories WHERE \"ProductId\" = {item.ProductId} AND (\"ProductVariantId\" = {item.ProductVariantId} OR (\"ProductVariantId\" IS NULL AND {item.ProductVariantId} IS NULL)) FOR UPDATE")
                    .FirstOrDefaultAsync(ct);

                if (inventory == null)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<IReadOnlyList<InventoryReservation>>(Error.NotFound("Inventory.NotFound", $"Inventory not found for product {item.ProductId}."));
                }

                // Critical Invariant: Available stock must be >= requested quantity
                if (inventory.AvailableQuantity < item.Quantity)
                {
                    _logger.LogWarning("Insufficient stock for Product {ProductId}. Available: {Available}, Requested: {Requested}",
                        item.ProductId, inventory.AvailableQuantity, item.Quantity);
                    await tx.RollbackAsync(ct);
                    return Result.Failure<IReadOnlyList<InventoryReservation>>(Error.Conflict("Inventory.InsufficientStock",
                        $"Insufficient stock for product {item.ProductId}. Available: {inventory.AvailableQuantity}, Requested: {item.Quantity}"));
                }

                // Mutate state atomically under lock
                inventory.ReservedQuantity += item.Quantity;
                inventory.UpdatedAtUtc = DateTime.UtcNow;

                var reservation = new InventoryReservation
                {
                    InventoryId = inventory.Id,
                    OrderId = orderId,
                    ProductId = item.ProductId,
                    ProductVariantId = item.ProductVariantId,
                    Quantity = item.Quantity,
                    Status = ReservationStatus.Reserved,
                    ExpiresAtUtc = expiresAtUtc
                };

                _dbContext.InventoryReservations.Add(reservation);
                reservations.Add(reservation);
            }

            await _dbContext.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            // Invalidate cached product details
            foreach (var item in sortedItems)
            {
                await _cacheService.RemoveAsync(CacheKeys.ProductById(item.ProductId), ct);
            }

            return Result.Success<IReadOnlyList<InventoryReservation>>(reservations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while reserving inventory for Order {OrderId}", orderId);
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Concurrency Strategy 2: Atomic conditional SQL UPDATE.
    /// Does not require pessimistic row locks; relies on atomic UPDATE ... WHERE (Quantity - ReservedQuantity >= @qty)
    /// </summary>
    public async Task<Result<IReadOnlyList<InventoryReservation>>> ReserveInventoryAtomicAsync(
        Guid orderId,
        IEnumerable<ReserveStockItem> items,
        TimeSpan? reservationTtl = null,
        CancellationToken ct = default)
    {
        var ttl = reservationTtl ?? TimeSpan.FromMinutes(15);
        var expiresAtUtc = DateTime.UtcNow.Add(ttl);
        var reservations = new List<InventoryReservation>();

        var sortedItems = items
            .OrderBy(i => i.ProductId)
            .ThenBy(i => i.ProductVariantId ?? Guid.Empty)
            .ToList();

        await using var tx = await _dbContext.Database.BeginTransactionAsync(ct);
        try
        {
            foreach (var item in sortedItems)
            {
                if (item.Quantity <= 0)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<IReadOnlyList<InventoryReservation>>(Error.Validation("Inventory.InvalidQuantity", "Quantity must be > 0."));
                }

                // Execute atomic update conditioned on sufficient available stock
                var rowsAffected = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE inventories SET \"ReservedQuantity\" = \"ReservedQuantity\" + {item.Quantity}, \"UpdatedAtUtc\" = {DateTime.UtcNow} WHERE \"ProductId\" = {item.ProductId} AND (\"ProductVariantId\" = {item.ProductVariantId} OR (\"ProductVariantId\" IS NULL AND {item.ProductVariantId} IS NULL)) AND (\"Quantity\" - \"ReservedQuantity\") >= {item.Quantity}", ct);

                if (rowsAffected == 0)
                {
                    _logger.LogWarning("Atomic reservation failed for Product {ProductId}. Stock depleted or insufficient.", item.ProductId);
                    await tx.RollbackAsync(ct);
                    return Result.Failure<IReadOnlyList<InventoryReservation>>(Error.Conflict("Inventory.InsufficientStock",
                        $"Insufficient stock for product {item.ProductId}. Could not reserve {item.Quantity} units."));
                }

                var inv = await _dbContext.Inventories
                    .AsNoTracking()
                    .FirstAsync(i => i.ProductId == item.ProductId && i.ProductVariantId == item.ProductVariantId, ct);

                var reservation = new InventoryReservation
                {
                    InventoryId = inv.Id,
                    OrderId = orderId,
                    ProductId = item.ProductId,
                    ProductVariantId = item.ProductVariantId,
                    Quantity = item.Quantity,
                    Status = ReservationStatus.Reserved,
                    ExpiresAtUtc = expiresAtUtc
                };

                _dbContext.InventoryReservations.Add(reservation);
                reservations.Add(reservation);
            }

            await _dbContext.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            foreach (var item in sortedItems)
            {
                await _cacheService.RemoveAsync(CacheKeys.ProductById(item.ProductId), ct);
            }

            return Result.Success<IReadOnlyList<InventoryReservation>>(reservations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Atomic reservation failed for Order {OrderId}", orderId);
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<Result> ReleaseReservationAsync(Guid orderId, CancellationToken ct = default)
    {
        await using var tx = await _dbContext.Database.BeginTransactionAsync(ct);

        var reservations = await _dbContext.InventoryReservations
            .Include(r => r.Inventory)
            .Where(r => r.OrderId == orderId && r.Status == ReservationStatus.Reserved)
            .ToListAsync(ct);

        if (!reservations.Any())
        {
            return Result.Success(); // Idempotent: already released or none found
        }

        foreach (var reservation in reservations)
        {
            reservation.Status = ReservationStatus.Released;
            reservation.UpdatedAtUtc = DateTime.UtcNow;

            if (reservation.Inventory != null)
            {
                reservation.Inventory.ReservedQuantity = Math.Max(0, reservation.Inventory.ReservedQuantity - reservation.Quantity);
                reservation.Inventory.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        await _dbContext.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        foreach (var res in reservations)
        {
            await _cacheService.RemoveAsync(CacheKeys.ProductById(res.ProductId), ct);
        }

        return Result.Success();
    }

    public async Task<Result> CommitReservationAsync(Guid orderId, CancellationToken ct = default)
    {
        await using var tx = await _dbContext.Database.BeginTransactionAsync(ct);

        var reservations = await _dbContext.InventoryReservations
            .Include(r => r.Inventory)
            .Where(r => r.OrderId == orderId && r.Status == ReservationStatus.Reserved)
            .ToListAsync(ct);

        if (!reservations.Any())
        {
            return Result.Failure(Error.NotFound("Inventory.NoReservation", $"No active reservations found for order {orderId}."));
        }

        foreach (var reservation in reservations)
        {
            reservation.Status = ReservationStatus.Committed;
            reservation.UpdatedAtUtc = DateTime.UtcNow;

            if (reservation.Inventory != null)
            {
                // Physical stock deducted, reserved stock decremented
                reservation.Inventory.Quantity = Math.Max(0, reservation.Inventory.Quantity - reservation.Quantity);
                reservation.Inventory.ReservedQuantity = Math.Max(0, reservation.Inventory.ReservedQuantity - reservation.Quantity);
                reservation.Inventory.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        await _dbContext.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        foreach (var res in reservations)
        {
            await _cacheService.RemoveAsync(CacheKeys.ProductById(res.ProductId), ct);
        }

        return Result.Success();
    }

    private static InventoryDto MapToDto(MultiVendorPlatform.Modules.Inventory.Domain.Inventory i) => new(
        i.Id,
        i.ProductId,
        i.ProductVariantId,
        i.Quantity,
        i.ReservedQuantity,
        i.AvailableQuantity,
        i.ReorderThreshold,
        i.AvailableQuantity <= i.ReorderThreshold);
}
