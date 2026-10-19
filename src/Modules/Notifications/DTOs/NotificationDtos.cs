using MultiVendorPlatform.Modules.Notifications.Domain;

namespace MultiVendorPlatform.Modules.Notifications.DTOs;

public record NotificationDto(
    Guid Id,
    Guid UserId,
    NotificationType Type,
    string Title,
    string Message,
    NotificationStatus Status,
    DateTime CreatedAtUtc,
    DateTime? SentAtUtc,
    DateTime? ReadAtUtc);
