using Microsoft.EntityFrameworkCore;
using MultiVendorPlatform.Infrastructure.Data;
using MultiVendorPlatform.Modules.Common.Results;
using MultiVendorPlatform.Modules.Notifications.Domain;
using MultiVendorPlatform.Modules.Notifications.DTOs;

namespace MultiVendorPlatform.Modules.Notifications.Services;

public interface INotificationService
{
    Task<Result<IReadOnlyList<NotificationDto>>> GetUserNotificationsAsync(Guid userId, bool unreadOnly = false, CancellationToken ct = default);
    Task<Result> MarkAsReadAsync(Guid notificationId, Guid userId, CancellationToken ct = default);
    Task<Result> SendNotificationAsync(Guid userId, NotificationType type, string title, string message, CancellationToken ct = default);
}

public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(ApplicationDbContext dbContext, ILogger<NotificationService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<NotificationDto>>> GetUserNotificationsAsync(Guid userId, bool unreadOnly = false, CancellationToken ct = default)
    {
        var query = _dbContext.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId);

        if (unreadOnly)
        {
            query = query.Where(n => n.Status != NotificationStatus.Read);
        }

        var notifications = await query
            .OrderByDescending(n => n.CreatedAtUtc)
            .Take(50)
            .Select(n => new NotificationDto(
                n.Id,
                n.UserId,
                n.Type,
                n.Title,
                n.Message,
                n.Status,
                n.CreatedAtUtc,
                n.SentAtUtc,
                n.ReadAtUtc))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<NotificationDto>>(notifications);
    }

    public async Task<Result> MarkAsReadAsync(Guid notificationId, Guid userId, CancellationToken ct = default)
    {
        var notification = await _dbContext.Notifications.FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId, ct);
        if (notification == null)
        {
            return Result.Failure(Error.NotFound("Notification.NotFound", "Notification not found."));
        }

        notification.Status = NotificationStatus.Read;
        notification.ReadAtUtc = DateTime.UtcNow;
        notification.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SendNotificationAsync(Guid userId, NotificationType type, string title, string message, CancellationToken ct = default)
    {
        var notification = new Notification
        {
            UserId = userId,
            Type = type,
            Title = title,
            Message = message,
            Status = NotificationStatus.Sent,
            SentAtUtc = DateTime.UtcNow
        };

        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync(ct);

        if (type == NotificationType.Email)
        {
            _logger.LogInformation("[EMAIL SIMULATION] To UserId: {UserId} | Subject: {Subject} | Body: {Body}",
                userId, title, message);
        }

        return Result.Success();
    }
}
