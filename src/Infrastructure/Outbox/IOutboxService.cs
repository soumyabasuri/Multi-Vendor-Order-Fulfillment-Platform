using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MultiVendorPlatform.Infrastructure.Data;
using MultiVendorPlatform.Infrastructure.Data.Entities;

namespace MultiVendorPlatform.Infrastructure.Outbox;

public interface IOutboxService
{
    void Enqueue<T>(T messagePayload, string topic, string eventType, string aggregateType, string aggregateId);
    Task<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(int batchSize, CancellationToken ct = default);
    Task MarkAsPublishedAsync(Guid messageId, CancellationToken ct = default);
    Task MarkAsFailedAsync(Guid messageId, string error, bool isDeadLetter, CancellationToken ct = default);
}

public class OutboxService : IOutboxService
{
    private readonly ApplicationDbContext _dbContext;

    public OutboxService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Enqueue<T>(T messagePayload, string topic, string eventType, string aggregateType, string aggregateId)
    {
        var json = JsonSerializer.Serialize(messagePayload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var message = new OutboxMessage
        {
            Topic = topic,
            EventType = eventType,
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            PayloadJson = json,
            Status = OutboxMessageStatus.Pending,
            RetryCount = 0,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.OutboxMessages.Add(message);
    }

    public async Task<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(int batchSize, CancellationToken ct = default)
    {
        return await _dbContext.OutboxMessages
            .Where(m => m.Status == OutboxMessageStatus.Pending)
            .OrderBy(m => m.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);
    }

    public async Task MarkAsPublishedAsync(Guid messageId, CancellationToken ct = default)
    {
        var message = await _dbContext.OutboxMessages.FindAsync(new object[] { messageId }, ct);
        if (message != null)
        {
            message.Status = OutboxMessageStatus.Published;
            message.ProcessedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(ct);
        }
    }

    public async Task MarkAsFailedAsync(Guid messageId, string error, bool isDeadLetter, CancellationToken ct = default)
    {
        var message = await _dbContext.OutboxMessages.FindAsync(new object[] { messageId }, ct);
        if (message != null)
        {
            message.RetryCount++;
            message.LastError = error;
            message.Status = isDeadLetter ? OutboxMessageStatus.DeadLetter : OutboxMessageStatus.Pending;
            message.UpdatedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(ct);
        }
    }
}
