using MultiVendorPlatform.Modules.Common.Domain;
using MultiVendorPlatform.Modules.Identity.Domain;

namespace MultiVendorPlatform.Modules.Notifications.Domain;

public enum NotificationType
{
    Email = 0,
    InApp = 1,
    Sms = 2
}

public enum NotificationStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
    Read = 3
}

public class Notification : BaseEntity<Guid>
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public NotificationType Type { get; set; } = NotificationType.InApp;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;

    public string? ErrorMessage { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTime? SentAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }

    public Notification()
    {
        Id = Guid.NewGuid();
    }
}
