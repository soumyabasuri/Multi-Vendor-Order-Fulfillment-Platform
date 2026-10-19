using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using MultiVendorPlatform.Infrastructure.Data;
using MultiVendorPlatform.Infrastructure.Outbox;
using MultiVendorPlatform.Modules.Cart.Domain;
using MultiVendorPlatform.Modules.Catalog.Domain;
using MultiVendorPlatform.Modules.Common.Results;
using MultiVendorPlatform.Modules.Identity.Domain;
using MultiVendorPlatform.Modules.Inventory.Domain;
using MultiVendorPlatform.Modules.Inventory.DTOs;
using MultiVendorPlatform.Modules.Inventory.Services;
using MultiVendorPlatform.Modules.Orders.Domain;
using MultiVendorPlatform.Modules.Orders.DTOs;
using MultiVendorPlatform.Modules.Orders.Services;
using MultiVendorPlatform.Modules.Vendors.Domain;
using Xunit;

namespace MultiVendorPlatform.UnitTests.Orders;

public class OrderServiceTests
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IInventoryService> _inventoryServiceMock;
    private readonly Mock<IOutboxService> _outboxServiceMock;
    private readonly Mock<ILogger<OrderService>> _loggerMock;
    private readonly OrderService _orderService;

    public OrderServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _inventoryServiceMock = new Mock<IInventoryService>();
        _outboxServiceMock = new Mock<IOutboxService>();
        _loggerMock = new Mock<ILogger<OrderService>>();

        _orderService = new OrderService(
            _dbContext,
            _inventoryServiceMock.Object,
            _outboxServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task CreateOrder_WhenCartIsEmpty_ReturnsValidationError()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var request = new CreateOrderRequest(new ShippingAddressDto("123 St", "City", "ST", "12345", "USA", "Customer", "555-1234"));

        // Act
        var result = await _orderService.CreateOrderAsync(customerId, request);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Cart.Empty");
    }

    [Fact]
    public async Task CreateOrder_WithMultipleVendors_SplitsIntoSeparateVendorOrdersAndComputesCommission()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var vendor1 = new Vendor { BusinessName = "Vendor Alpha", Slug = "alpha", CommissionRate = 0.10m };
        var vendor2 = new Vendor { BusinessName = "Vendor Beta", Slug = "beta", CommissionRate = 0.15m };
        _dbContext.Vendors.AddRange(vendor1, vendor2);

        var product1 = new Product { VendorId = vendor1.Id, Vendor = vendor1, Name = "Item A", Sku = "SKU-A", BasePrice = 50m, IsActive = true };
        var product2 = new Product { VendorId = vendor2.Id, Vendor = vendor2, Name = "Item B", Sku = "SKU-B", BasePrice = 100m, IsActive = true };
        _dbContext.Products.AddRange(product1, product2);

        var cart = new Cart { CustomerId = customerId };
        cart.Items.Add(new CartItem { CartId = cart.Id, ProductId = product1.Id, Product = product1, Quantity = 2, UnitPrice = 50m }); // Subtotal $100
        cart.Items.Add(new CartItem { CartId = cart.Id, ProductId = product2.Id, Product = product2, Quantity = 1, UnitPrice = 100m }); // Subtotal $100
        _dbContext.Carts.Add(cart);
        await _dbContext.SaveChangesAsync();

        _inventoryServiceMock.Setup(s => s.ReserveInventoryAsync(
            It.IsAny<Guid>(),
            It.IsAny<IEnumerable<ReserveStockItem>>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<InventoryReservation>>(new List<InventoryReservation>()));

        var request = new CreateOrderRequest(new ShippingAddressDto("123 Main", "City", "CA", "90210", "US", "John Doe", "555-0100"));

        // Act
        var result = await _orderService.CreateOrderAsync(customerId, request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var order = result.Value;

        order.VendorOrders.Should().HaveCount(2);

        // Vendor 1 calculations
        var vo1 = order.VendorOrders.Single(vo => vo.VendorId == vendor1.Id);
        vo1.SubtotalAmount.Should().Be(100m);
        vo1.CommissionAmount.Should().Be(10m); // 10%
        vo1.PayoutAmount.Should().Be(90m);     // 100 - 10

        // Vendor 2 calculations
        var vo2 = order.VendorOrders.Single(vo => vo.VendorId == vendor2.Id);
        vo2.SubtotalAmount.Should().Be(100m);
        vo2.CommissionAmount.Should().Be(15m); // 15%
        vo2.PayoutAmount.Should().Be(85m);     // 100 - 15

        // Outbox event enqueued
        _outboxServiceMock.Verify(o => o.Enqueue(
            It.IsAny<object>(),
            "orders.order-created",
            "OrderCreatedEvent",
            "Order",
            order.Id.ToString()), Times.Once);

        // Cart items cleared
        var remainingCartItems = await _dbContext.CartItems.Where(ci => ci.CartId == cart.Id).ToListAsync();
        remainingCartItems.Should().BeEmpty();
    }
}
