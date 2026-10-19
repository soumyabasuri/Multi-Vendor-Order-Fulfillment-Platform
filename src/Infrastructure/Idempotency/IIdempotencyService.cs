using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MultiVendorPlatform.Infrastructure.Data;
using MultiVendorPlatform.Infrastructure.Data.Entities;
using MultiVendorPlatform.Modules.Common.Results;

namespace MultiVendorPlatform.Infrastructure.Idempotency;

public interface IIdempotencyService
{
    Task<Result<IdempotencyRecord?>> GetRecordAsync(string idempotencyKey, string requestPath, string requestPayload, CancellationToken ct = default);
    Task SaveRecordAsync(string idempotencyKey, string requestPath, string requestPayload, int statusCode, object responseBody, TimeSpan? ttl = null, CancellationToken ct = default);
}

public class IdempotencyService : IIdempotencyService
{
    private readonly ApplicationDbContext _dbContext;

    public IdempotencyService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IdempotencyRecord?>> GetRecordAsync(string idempotencyKey, string requestPath, string requestPayload, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Result.Success<IdempotencyRecord?>(null);
        }

        var record = await _dbContext.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, ct);

        if (record == null)
        {
            return Result.Success<IdempotencyRecord?>(null);
        }

        // Verify request hash matches to prevent key collision with differing payloads
        var currentHash = ComputeSha256(requestPayload);
        if (!string.Equals(record.RequestHash, currentHash, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<IdempotencyRecord?>(Error.Conflict(
                "Idempotency.PayloadMismatch",
                "Idempotency key has already been used with a different request payload."));
        }

        return Result.Success<IdempotencyRecord?>(record);
    }

    public async Task SaveRecordAsync(
        string idempotencyKey,
        string requestPath,
        string requestPayload,
        int statusCode,
        object responseBody,
        TimeSpan? ttl = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey)) return;

        var existing = await _dbContext.IdempotencyRecords.FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, ct);
        if (existing != null) return;

        var json = responseBody is string strBody
            ? strBody
            : JsonSerializer.Serialize(responseBody, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var record = new IdempotencyRecord
        {
            IdempotencyKey = idempotencyKey,
            RequestPath = requestPath,
            RequestHash = ComputeSha256(requestPayload),
            ResponseStatusCode = statusCode,
            ResponseBodyJson = json,
            ExpiresAtUtc = DateTime.UtcNow.Add(ttl ?? TimeSpan.FromHours(24)),
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.IdempotencyRecords.Add(record);
        await _dbContext.SaveChangesAsync(ct);
    }

    public static string ComputeSha256(string content)
    {
        if (string.IsNullOrEmpty(content)) content = string.Empty;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }
}
