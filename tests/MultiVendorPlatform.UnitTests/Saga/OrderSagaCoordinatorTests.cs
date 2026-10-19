using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using MultiVendorPlatform.Infrastructure.Data;
using MultiVendorPlatform.Infrastructure.Kafka;
using MultiVendorPlatform.Infrastructure.Outbox;
using MultiVendorPlatform.Modules.Common.Results;
using MultiVendorPlatform.Modules.Inventory.Services;
using MultiVendorPlatform.Modules.Orders.Domain;
using MultiVendorPlatform.Modules.Orders.Services;
using MultiVendorPlatform.Modules.Vendors.Domain;
using Xunit;

namespace MultiVendorPlatform.UnitTests.Saga;

public class OrderSagaCoordinatorTests
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IInventoryService> _inventoryServiceMock;
    private readonly Mock<IOutboxService> _outboxServiceMock;
    private readonly Mock<ILogger<OrderSagaCoordinator>> _loggerMock;
    private readonly OrderSagaCoordinator _sagaCoordinator;

    public OrderSagaCoordinatorTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _inventoryServiceMock = new Mock<IInventoryService>();
        _outboxServiceMock = new Mock<IOutboxService>();
        _loggerMock = new Mock<ILogger<OrderSagaCoordinator>>();

        _sagaCoordinator = new OrderSagaCoordinator(
            _dbContext,
            _inventoryServiceMock.Object,
            _outboxServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task HandlePaymentCompleted_ConfirmsOrder_AndDispatchesVendorTasks()
    {
        // Arrange
        var vendor = new Vendor { BusinessName = "Vendor X", OwnerUserId = Guid.NewGuid() };
        _dbContext.Vendors.Add(vendor);

        var order = new Order
        {
            OrderNumber = "ORD-TEST-123",
            CustomerId = Guid.NewGuid(),
            Status = OrderStatus.PaymentPending,
            TotalAmount = 150m
        };

        var vendorOrder = new VendorOrder
        {
            OrderId = order.Id,
            VendorId = vendor.Id,
            Vendor = vendor,
            Status = VendorOrderStatus.Pending,
            SubtotalAmount = 150m
        };
        order.VendorOrders.Add(vendorOrder);

        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        _inventoryServiceMock.Setup(s => s.CommitReservationAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var paymentCompletedEvent = new PaymentCompletedEvent(
            Guid.NewGuid(),
            order.Id,
            "TXN-9999",
            150m,
            DateTime.UtcNow);

        // Act
        await _sagaCoordinator.HandlePaymentCompletedAsync(paymentCompletedEvent);

        // Assert
        var updatedOrder = await _dbContext.Orders.Include(o => o.VendorOrders).FirstAsync(o => o.Id == order.Id);
        updatedOrder.Status.Should().Be(OrderStatus.Confirmed);
        updatedOrder.VendorOrders.First().Status.Should().Be(VendorOrderStatus.Preparing);

        _inventoryServiceMock.Verify(s => s.CommitReservationAsync(order.Id, It.IsAny<CancellationToken>()), Times.Once);

        _outboxServiceMock.Verify(o => o.Enqueue(
            It.IsAny<OrderConfirmedEvent>(),
            "orders.order-confirmed",
            "OrderConfirmedEvent",
            "Order",
            order.Id.ToString()), Times.Once);
    }

    [Fact]
    public async Task HandlePaymentFailed_ExecutesCompensatingTransaction_ReleasesInventoryAndMarksOrderFailed()
    {
        // Arrange
        var order = new Order
        {
            OrderNumber = "ORD-FAIL-456",
            CustomerId = Guid.NewGuid(),
            Status = OrderStatus.PaymentPending,
            TotalAmount = 75m
        };
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        _inventoryServiceMock.Setup(s => s.ReleaseReservationAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var paymentFailedEvent = new PaymentFailedEvent(
            Guid.NewGuid(),
            order.Id,
            "Card expired or declined",
            DateTime.UtcNow);

        // Act
        await _sagaCoordinator.HandlePaymentFailedAsync(paymentFailedEvent);

        // Assert
        var updatedOrder = await _dbContext.Orders.FirstAsync(o => o.Id == order.Id);
        updatedOrder.Status.Should().Be(OrderStatus.Failed);
        updatedOrder.CancellationReason.Should().Contain("Card expired or declined");

        // Verifies compensating transaction was executed
        _inventoryServiceMock.Verify(s => s.ReleaseReservationAsync(order.Id, It.IsAny<CancellationToken>()), Times.Once);

        _outboxServiceMock.Verify(o => o.Enqueue(
            It.IsAny<InventoryReleasedEvent>(),
            "inventory.inventory-released",
            "InventoryReleasedEvent",
            "Order",
            order.Id.ToString()), Times.Once);
    }
}
