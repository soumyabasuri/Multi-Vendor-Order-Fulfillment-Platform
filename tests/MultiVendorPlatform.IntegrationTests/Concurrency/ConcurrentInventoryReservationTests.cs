using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using MultiVendorPlatform.Infrastructure.Caching;
using MultiVendorPlatform.Infrastructure.Data;
using MultiVendorPlatform.Modules.Catalog.Domain;
using MultiVendorPlatform.Modules.Inventory.Domain;
using MultiVendorPlatform.Modules.Inventory.DTOs;
using MultiVendorPlatform.Modules.Inventory.Services;
using MultiVendorPlatform.Modules.Vendors.Domain;
using Xunit;

namespace MultiVendorPlatform.IntegrationTests.Concurrency;

public class ConcurrentInventoryReservationTests
{
    [Fact]
    public async Task ConcurrentReservations_100RequestsFor10AvailableUnits_NeverExceedsStockAndNeverGoesNegative()
    {
        // Arrange: Setup Database with 10 units of initial inventory
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        var productId = Guid.NewGuid();
        var vendorId = Guid.NewGuid();

        using (var setupDb = new ApplicationDbContext(options))
        {
            var vendor = new Vendor { Id = vendorId, BusinessName = "Vendor Test", Slug = "vendor-test" };
            var product = new Product { Id = productId, VendorId = vendorId, Name = "High-Demand Gadget", Sku = "FLASH-100", BasePrice = 99m };
            var inventory = new MultiVendorPlatform.Modules.Inventory.Domain.Inventory
            {
                ProductId = productId,
                Quantity = 10,         // Exactly 10 units physically in stock
                ReservedQuantity = 0   // 0 initially reserved => 10 available
            };

            setupDb.Vendors.Add(vendor);
            setupDb.Products.Add(product);
            setupDb.Inventories.Add(inventory);
            await setupDb.SaveChangesAsync();
        }

        var cacheMock = new Mock<ICacheService>();
        var loggerMock = new Mock<ILogger<InventoryService>>();

        const int totalConcurrentRequests = 100;
        const int unitsPerRequest = 1;

        var successCount = 0;
        var failureCount = 0;
        var failureErrors = new ConcurrentBag<string>();

        // Act: Fire 100 simultaneous concurrent tasks attempting to reserve 1 unit
        var tasks = Enumerable.Range(0, totalConcurrentRequests).Select(async i =>
        {
            // Each concurrent request uses its own DbContext instance simulating separate HTTP worker threads
            using var requestDb = new ApplicationDbContext(options);
            var service = new InventoryService(requestDb, cacheMock.Object, loggerMock.Object);
            var orderId = Guid.NewGuid();

            var reserveItem = new ReserveStockItem(productId, null, unitsPerRequest);
            var result = await service.ReserveInventoryAsync(orderId, new[] { reserveItem });

            if (result.IsSuccess)
            {
                Interlocked.Increment(ref successCount);
            }
            else
            {
                Interlocked.Increment(ref failureCount);
                failureErrors.Add(result.Error.Code);
            }
        });

        await Task.WhenAll(tasks);

        // Assert: Verify state invariants
        using (var verifyDb = new ApplicationDbContext(options))
        {
            var finalInventory = await verifyDb.Inventories.SingleAsync(inv => inv.ProductId == productId);

            // 1. Physical quantity unchanged (physical deduction occurs upon payment commit)
            finalInventory.Quantity.Should().Be(10);

            // 2. Exactly 10 units reserved, NEVER more than initial stock!
            finalInventory.ReservedQuantity.Should().Be(10);

            // 3. Available stock is exactly 0, NEVER negative!
            finalInventory.AvailableQuantity.Should().Be(0);

            // 4. Invariant check
            (finalInventory.AvailableQuantity >= 0).Should().BeTrue();
            (finalInventory.Quantity >= finalInventory.ReservedQuantity).Should().BeTrue();

            // 5. Exactly 10 concurrent requests succeeded and 90 were rejected
            successCount.Should().Be(10, "exactly 10 requests should succeed for 10 units of stock");
            failureCount.Should().Be(90, "the remaining 90 requests should be safely rejected with InsufficientStock");
            failureErrors.Should().OnlyContain(code => code == "Inventory.InsufficientStock");
        }
    }
}
