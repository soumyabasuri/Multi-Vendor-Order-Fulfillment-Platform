using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MultiVendorPlatform.Infrastructure.Data;
using MultiVendorPlatform.Modules.Notifications.Domain;

namespace MultiVendorPlatform.Infrastructure.BackgroundJobs;

public class LowStockMonitoringBackgroundJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LowStockMonitoringBackgroundJob> _logger;

    public LowStockMonitoringBackgroundJob(
        IServiceScopeFactory scopeFactory,
        ILogger<LowStockMonitoringBackgroundJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Low Stock Monitoring Background Job initialized.");
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ScanLowStockAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scanning for low stock items.");
            }
        }
    }

    private async Task ScanLowStockAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var lowStockInventories = await dbContext.Inventories
            .Include(i => i.Product)
            .ThenInclude(p => p.Vendor)
            .Where(i => (i.Quantity - i.ReservedQuantity) <= i.ReorderThreshold && i.Product.IsActive)
            .Take(50)
            .ToListAsync(ct);

        if (!lowStockInventories.Any()) return;

        _logger.LogInformation("Detected {Count} items below reorder threshold.", lowStockInventories.Count);

        foreach (var inv in lowStockInventories)
        {
            var vendorOwnerId = inv.Product.Vendor.OwnerUserId;
            var available = inv.Quantity - inv.ReservedQuantity;

            // Check if alert already sent in the last 24 hours to avoid spamming
            var oneDayAgo = DateTime.UtcNow.AddDays(-1);
            var alreadyAlerted = await dbContext.Notifications
                .AnyAsync(n => n.UserId == vendorOwnerId &&
                               n.Title.Contains("Low Stock Alert") &&
                               n.Message.Contains(inv.Product.Name) &&
                               n.CreatedAtUtc >= oneDayAgo, ct);

            if (!alreadyAlerted)
            {
                var notification = new Notification
                {
                    UserId = vendorOwnerId,
                    Type = NotificationType.InApp,
                    Title = "Low Stock Alert",
                    Message = $"Product '{inv.Product.Name}' (SKU: {inv.Product.Sku}) is running low on stock. Available: {available}, Threshold: {inv.ReorderThreshold}.",
                    Status = NotificationStatus.Sent,
                    SentAtUtc = DateTime.UtcNow
                };

                dbContext.Notifications.Add(notification);
            }
        }

        await dbContext.SaveChangesAsync(ct);
    }
}
