using MultiVendorPlatform.Modules.Common.Domain;

namespace MultiVendorPlatform.Infrastructure.Data.Entities;

public enum OutboxMessageStatus
{
    Pending = 0,
    Processing = 1,
    Published = 2,
    DeadLetter = 3
}

public class OutboxMessage : BaseEntity<Guid>
{
    public string EventType { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string AggregateType { get; set; } = string.Empty;
    public string AggregateId { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public OutboxMessageStatus Status { get; set; } = OutboxMessageStatus.Pending;

    public int RetryCount { get; set; } = 0;
    public string? LastError { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }

    public OutboxMessage()
    {
        Id = Guid.NewGuid();
    }
}

public class IdempotencyRecord : BaseEntity<Guid>
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestPath { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public int ResponseStatusCode { get; set; }
    public string ResponseBodyJson { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }

    public IdempotencyRecord()
    {
        Id = Guid.NewGuid();
    }
}

public class AuditLog : BaseEntity<Guid>
{
    public Guid? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string ChangesJson { get; set; } = "{}";
    public string? IpAddress { get; set; }

    public AuditLog()
    {
        Id = Guid.NewGuid();
    }
}
