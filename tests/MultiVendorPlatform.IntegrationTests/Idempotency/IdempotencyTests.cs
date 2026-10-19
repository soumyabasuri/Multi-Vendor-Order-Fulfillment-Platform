using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MultiVendorPlatform.Infrastructure.Data;
using MultiVendorPlatform.Infrastructure.Idempotency;
using Xunit;

namespace MultiVendorPlatform.IntegrationTests.Idempotency;

public class IdempotencyTests
{
    [Fact]
    public async Task IdempotencyService_DuplicateRequest_ReturnsCachedRecord()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var dbContext = new ApplicationDbContext(options);
        var service = new IdempotencyService(dbContext);

        var key = Guid.NewGuid().ToString("N");
        var path = "/api/orders";
        var payload = "{\"shippingAddress\":{\"street\":\"123 Market St\"}}";
        var responseBody = new { orderId = Guid.NewGuid(), status = "Pending" };

        // Act 1: Save first execution result
        await service.SaveRecordAsync(key, path, payload, 200, responseBody);

        // Act 2: Second execution arrives with same key & same payload
        var recordResult = await service.GetRecordAsync(key, path, payload);

        // Assert
        recordResult.IsSuccess.Should().BeTrue();
        recordResult.Value.Should().NotBeNull();
        recordResult.Value!.ResponseStatusCode.Should().Be(200);
        recordResult.Value.ResponseBodyJson.Should().Contain("Pending");
    }

    [Fact]
    public async Task IdempotencyService_ReusingKeyWithDifferentPayload_ReturnsConflictError()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var dbContext = new ApplicationDbContext(options);
        var service = new IdempotencyService(dbContext);

        var key = Guid.NewGuid().ToString("N");
        var path = "/api/orders";
        var originalPayload = "{\"total\": 100}";
        var alteredPayload = "{\"total\": 200}";

        await service.SaveRecordAsync(key, path, originalPayload, 200, new { ok = true });

        // Act: Attempt to lookup using altered payload
        var result = await service.GetRecordAsync(key, path, alteredPayload);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Idempotency.PayloadMismatch");
    }
}
