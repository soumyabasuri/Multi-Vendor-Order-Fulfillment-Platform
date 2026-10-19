using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MultiVendorPlatform.Infrastructure.Data;
using MultiVendorPlatform.Infrastructure.Kafka;
using MultiVendorPlatform.Infrastructure.Outbox;
using MultiVendorPlatform.Modules.Inventory.Domain;
using MultiVendorPlatform.Modules.Orders.Domain;

namespace MultiVendorPlatform.Infrastructure.BackgroundJobs;

public class ReservationExpiryBackgroundJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReservationExpiryBackgroundJob> _logger;

    public ReservationExpiryBackgroundJob(
        IServiceScopeFactory scopeFactory,
        ILogger<ReservationExpiryBackgroundJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Reservation Expiry Background Job initialized.");
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepExpiredReservationsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during reservation expiry sweep.");
            }
        }
    }

    private async Task SweepExpiredReservationsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var outboxService = scope.ServiceProvider.GetRequiredService<IOutboxService>();

        var now = DateTime.UtcNow;
        var expiredReservations = await dbContext.InventoryReservations
            .Include(r => r.Inventory)
            .Where(r => r.Status == ReservationStatus.Reserved && r.ExpiresAtUtc <= now)
            .Take(100)
            .ToListAsync(ct);

        if (!expiredReservations.Any()) return;

        _logger.LogInformation("Found {Count} expired inventory reservations to release.", expiredReservations.Count);

        var orderIds = expiredReservations.Select(r => r.OrderId).Distinct().ToList();

        await using var tx = await dbContext.Database.BeginTransactionAsync(ct);
        try
        {
            foreach (var reservation in expiredReservations)
            {
                reservation.Status = ReservationStatus.Expired;
                reservation.UpdatedAtUtc = now;

                if (reservation.Inventory != null)
                {
                    reservation.Inventory.ReservedQuantity = Math.Max(0, reservation.Inventory.ReservedQuantity - reservation.Quantity);
                    reservation.Inventory.UpdatedAtUtc = now;
                }
            }

            // Fail orders that were pending payment
            var orders = await dbContext.Orders
                .Where(o => orderIds.Contains(o.Id) &&
                            (o.Status == OrderStatus.InventoryReserved || o.Status == OrderStatus.PaymentPending))
                .ToListAsync(ct);

            foreach (var order in orders)
            {
                order.Status = OrderStatus.Failed;
                order.CancellationReason = "Reservation expired: Payment was not completed within the time window.";
                order.UpdatedAtUtc = now;

                var releasedEvent = new InventoryReleasedEvent(order.Id, "Reservation expired", now);
                outboxService.Enqueue(releasedEvent, KafkaTopics.InventoryReleased, nameof(InventoryReleasedEvent), "Order", order.Id.ToString());
            }

            await dbContext.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation("Successfully released {Count} expired reservations and expired {OrderCount} pending orders.",
                expiredReservations.Count, orders.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete expired reservations sweep.");
            await tx.RollbackAsync(ct);
        }
    }
}
