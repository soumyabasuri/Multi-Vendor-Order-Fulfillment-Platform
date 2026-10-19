using Microsoft.EntityFrameworkCore;
using MultiVendorPlatform.Infrastructure.Data;
using MultiVendorPlatform.Modules.Common.Results;
using MultiVendorPlatform.Modules.Identity.Domain;
using MultiVendorPlatform.Modules.Identity.DTOs;
using MultiVendorPlatform.Modules.Identity.Security;

namespace MultiVendorPlatform.Modules.Identity.Services;

public interface IIdentityService
{
    Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request, string? ipAddress, CancellationToken ct = default);
    Task<Result<AuthResponse>> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken ct = default);
    Task<Result<AuthResponse>> RefreshTokenAsync(string refreshToken, string? ipAddress, CancellationToken ct = default);
    Task<Result> RevokeTokenAsync(string refreshToken, string? ipAddress, CancellationToken ct = default);
    Task<Result<UserDto>> GetProfileAsync(Guid userId, CancellationToken ct = default);
}

public class IdentityService : IIdentityService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;

    public IdentityService(
        ApplicationDbContext dbContext,
        IPasswordHasher _passwordHasher,
        IJwtTokenService _jwtTokenService)
    {
        _dbContext = dbContext;
        this._passwordHasher = _passwordHasher;
        this._jwtTokenService = _jwtTokenService;
    }

    public async Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request, string? ipAddress, CancellationToken ct = default)
    {
        var normalizedEmail = request.Email.ToUpperInvariant();
        var exists = await _dbContext.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail, ct);
        if (exists)
        {
            return Result.Failure<AuthResponse>(Error.Conflict("Identity.EmailInUse", "Email address is already registered."));
        }

        var user = new User
        {
            Email = request.Email,
            NormalizedEmail = normalizedEmail,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.PhoneNumber,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            VendorId = request.VendorId
        };

        var roleName = string.IsNullOrWhiteSpace(request.Role) ? Role.Customer : request.Role;
        var role = await _dbContext.Roles.FirstOrDefaultAsync(r => r.NormalizedName == roleName.ToUpperInvariant(), ct);
        if (role == null)
        {
            return Result.Failure<AuthResponse>(Error.NotFound("Identity.RoleNotFound", $"Role '{roleName}' does not exist."));
        }

        user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });

        var refreshToken = _jwtTokenService.GenerateRefreshToken(user.Id, ipAddress);
        user.RefreshTokens.Add(refreshToken);

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(ct);

        var (accessToken, expiresAtUtc) = _jwtTokenService.GenerateAccessToken(user, new[] { role.Name });

        var userDto = new UserDto(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.PhoneNumber,
            new List<string> { role.Name },
            user.VendorId);

        return Result.Success(new AuthResponse(accessToken, refreshToken.Token, expiresAtUtc, userDto));
    }

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken ct = default)
    {
        var normalizedEmail = request.Email.ToUpperInvariant();
        var user = await _dbContext.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);

        if (user == null || !_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            return Result.Failure<AuthResponse>(Error.Unauthorized("Identity.InvalidCredentials", "Invalid email or password."));
        }

        if (!user.IsActive)
        {
            return Result.Failure<AuthResponse>(Error.Forbidden("Identity.AccountDisabled", "Your account has been deactivated."));
        }

        var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();
        var (accessToken, expiresAtUtc) = _jwtTokenService.GenerateAccessToken(user, roles);

        var refreshToken = _jwtTokenService.GenerateRefreshToken(user.Id, ipAddress);
        _dbContext.RefreshTokens.Add(refreshToken);

        await _dbContext.SaveChangesAsync(ct);

        var userDto = new UserDto(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.PhoneNumber,
            roles,
            user.VendorId);

        return Result.Success(new AuthResponse(accessToken, refreshToken.Token, expiresAtUtc, userDto));
    }

    public async Task<Result<AuthResponse>> RefreshTokenAsync(string token, string? ipAddress, CancellationToken ct = default)
    {
        var user = await _dbContext.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.RefreshTokens.Any(t => t.Token == token), ct);

        if (user == null)
        {
            return Result.Failure<AuthResponse>(Error.Unauthorized("Identity.InvalidToken", "Refresh token was not found."));
        }

        var refreshToken = user.RefreshTokens.Single(t => t.Token == token);

        if (refreshToken.IsRevoked)
        {
            // Security measure: Revoke all ancestor tokens if a revoked token is re-presented (token reuse detection)
            foreach (var t in user.RefreshTokens.Where(x => x.IsActive))
            {
                t.RevokedAtUtc = DateTime.UtcNow;
                t.RevokedByIp = ipAddress;
            }
            await _dbContext.SaveChangesAsync(ct);
            return Result.Failure<AuthResponse>(Error.Unauthorized("Identity.TokenCompromised", "Attempted token reuse detected. All sessions terminated."));
        }

        if (refreshToken.IsExpired)
        {
            return Result.Failure<AuthResponse>(Error.Unauthorized("Identity.TokenExpired", "Refresh token has expired."));
        }

        // Rotate token
        var newRefreshToken = _jwtTokenService.GenerateRefreshToken(user.Id, ipAddress);
        refreshToken.RevokedAtUtc = DateTime.UtcNow;
        refreshToken.RevokedByIp = ipAddress;
        refreshToken.ReplacedByToken = newRefreshToken.Token;

        _dbContext.RefreshTokens.Add(newRefreshToken);
        await _dbContext.SaveChangesAsync(ct);

        var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();
        var (accessToken, expiresAtUtc) = _jwtTokenService.GenerateAccessToken(user, roles);

        var userDto = new UserDto(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.PhoneNumber,
            roles,
            user.VendorId);

        return Result.Success(new AuthResponse(accessToken, newRefreshToken.Token, expiresAtUtc, userDto));
    }

    public async Task<Result> RevokeTokenAsync(string token, string? ipAddress, CancellationToken ct = default)
    {
        var user = await _dbContext.Users
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.RefreshTokens.Any(t => t.Token == token), ct);

        if (user == null)
        {
            return Result.Failure(Error.NotFound("Identity.TokenNotFound", "Token not found."));
        }

        var refreshToken = user.RefreshTokens.Single(t => t.Token == token);
        if (!refreshToken.IsActive)
        {
            return Result.Failure(Error.Conflict("Identity.TokenInactive", "Token is already revoked or expired."));
        }

        refreshToken.RevokedAtUtc = DateTime.UtcNow;
        refreshToken.RevokedByIp = ipAddress;
        await _dbContext.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result<UserDto>> GetProfileAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _dbContext.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user == null)
        {
            return Result.Failure<UserDto>(Error.NotFound("Identity.UserNotFound", "User not found."));
        }

        var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();
        return Result.Success(new UserDto(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.PhoneNumber,
            roles,
            user.VendorId));
    }
}
