using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MultiVendorPlatform.Infrastructure.Data;
using MultiVendorPlatform.Modules.Common.Models;
using MultiVendorPlatform.Modules.Common.Results;
using MultiVendorPlatform.Modules.Identity.Domain;
using MultiVendorPlatform.Modules.Vendors.Domain;
using MultiVendorPlatform.Modules.Vendors.DTOs;

namespace MultiVendorPlatform.Modules.Vendors.Services;

public interface IVendorService
{
    Task<Result<VendorDto>> RegisterVendorAsync(Guid ownerUserId, RegisterVendorRequest request, CancellationToken ct = default);
    Task<Result<VendorDto>> GetVendorByIdAsync(Guid vendorId, CancellationToken ct = default);
    Task<Result<VendorDto>> GetVendorByOwnerIdAsync(Guid ownerUserId, CancellationToken ct = default);
    Task<Result<PagedResult<VendorDto>>> GetVendorsAsync(VendorStatus? status, int pageNumber, int pageSize, CancellationToken ct = default);
    Task<Result<VendorDto>> UpdateVendorProfileAsync(Guid vendorId, Guid requestingUserId, UpdateVendorRequest request, CancellationToken ct = default);
    Task<Result<VendorDto>> ReviewVendorAsync(Guid vendorId, ApproveVendorRequest request, CancellationToken ct = default);
}

public class VendorService : IVendorService
{
    private readonly ApplicationDbContext _dbContext;

    public VendorService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<VendorDto>> RegisterVendorAsync(Guid ownerUserId, RegisterVendorRequest request, CancellationToken ct = default)
    {
        var user = await _dbContext.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == ownerUserId, ct);

        if (user == null)
        {
            return Result.Failure<VendorDto>(Error.NotFound("User.NotFound", "User not found."));
        }

        var existingVendor = await _dbContext.Vendors.FirstOrDefaultAsync(v => v.OwnerUserId == ownerUserId, ct);
        if (existingVendor != null)
        {
            return Result.Failure<VendorDto>(Error.Conflict("Vendor.AlreadyExists", "User is already associated with a vendor."));
        }

        var slug = GenerateSlug(request.BusinessName);
        var slugExists = await _dbContext.Vendors.AnyAsync(v => v.Slug == slug, ct);
        if (slugExists)
        {
            slug = $"{slug}-{Guid.NewGuid().ToString()[..6]}";
        }

        var vendor = new Vendor
        {
            OwnerUserId = ownerUserId,
            BusinessName = request.BusinessName,
            Slug = slug,
            Description = request.Description,
            ContactEmail = request.ContactEmail,
            ContactPhone = request.ContactPhone,
            CommissionRate = request.CommissionRate ?? 0.10m,
            Status = VendorStatus.PendingApproval
        };

        // Link user to vendor and assign Vendor role if not assigned
        user.VendorId = vendor.Id;
        var vendorRole = await _dbContext.Roles.FirstOrDefaultAsync(r => r.NormalizedName == Role.Vendor.ToUpperInvariant(), ct);
        if (vendorRole != null && !user.UserRoles.Any(ur => ur.RoleId == vendorRole.Id))
        {
            user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = vendorRole.Id });
        }

        _dbContext.Vendors.Add(vendor);
        await _dbContext.SaveChangesAsync(ct);

        return Result.Success(MapToDto(vendor));
    }

    public async Task<Result<VendorDto>> GetVendorByIdAsync(Guid vendorId, CancellationToken ct = default)
    {
        var vendor = await _dbContext.Vendors.AsNoTracking().FirstOrDefaultAsync(v => v.Id == vendorId, ct);
        if (vendor == null)
        {
            return Result.Failure<VendorDto>(Error.NotFound("Vendor.NotFound", "Vendor not found."));
        }

        return Result.Success(MapToDto(vendor));
    }

    public async Task<Result<VendorDto>> GetVendorByOwnerIdAsync(Guid ownerUserId, CancellationToken ct = default)
    {
        var vendor = await _dbContext.Vendors.FirstOrDefaultAsync(v => v.OwnerUserId == ownerUserId, ct);
        if (vendor == null)
        {
            var user = await _dbContext.Users
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.Id == ownerUserId, ct);

            if (user != null && user.UserRoles.Any(ur => ur.Role.Name == Role.Vendor))
            {
                vendor = new Vendor
                {
                    Id = Guid.NewGuid(),
                    OwnerUserId = user.Id,
                    BusinessName = $"{user.FirstName}'s Store",
                    Slug = $"{user.FirstName.ToLowerInvariant()}-{Guid.NewGuid().ToString("N")[..6]}",
                    ContactEmail = user.Email,
                    Status = VendorStatus.Approved,
                    CommissionRate = 0.10m
                };
                _dbContext.Vendors.Add(vendor);
                user.VendorId = vendor.Id;
                await _dbContext.SaveChangesAsync(ct);
            }
        }

        if (vendor == null)
        {
            return Result.Failure<VendorDto>(Error.NotFound("Vendor.NotFound", "Vendor for user not found."));
        }

        return Result.Success(MapToDto(vendor));
    }

    public async Task<Result<PagedResult<VendorDto>>> GetVendorsAsync(VendorStatus? status, int pageNumber, int pageSize, CancellationToken ct = default)
    {
        var query = _dbContext.Vendors.AsNoTracking().AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(v => v.Status == status.Value);
        }

        var totalCount = await query.CountAsync(ct);
        var vendors = await query
            .OrderByDescending(v => v.CreatedAtUtc)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var dtos = vendors.Select(MapToDto).ToList();
        return Result.Success(new PagedResult<VendorDto>(dtos, totalCount, pageNumber, pageSize));
    }

    public async Task<Result<VendorDto>> UpdateVendorProfileAsync(Guid vendorId, Guid requestingUserId, UpdateVendorRequest request, CancellationToken ct = default)
    {
        var vendor = await _dbContext.Vendors.FirstOrDefaultAsync(v => v.Id == vendorId, ct);
        if (vendor == null)
        {
            return Result.Failure<VendorDto>(Error.NotFound("Vendor.NotFound", "Vendor not found."));
        }

        if (vendor.OwnerUserId != requestingUserId)
        {
            return Result.Failure<VendorDto>(Error.Forbidden("Vendor.NotAuthorized", "You do not own this vendor profile."));
        }

        vendor.BusinessName = request.BusinessName;
        vendor.Description = request.Description;
        vendor.ContactEmail = request.ContactEmail;
        vendor.ContactPhone = request.ContactPhone;
        vendor.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(ct);
        return Result.Success(MapToDto(vendor));
    }

    public async Task<Result<VendorDto>> ReviewVendorAsync(Guid vendorId, ApproveVendorRequest request, CancellationToken ct = default)
    {
        var vendor = await _dbContext.Vendors.FirstOrDefaultAsync(v => v.Id == vendorId, ct);
        if (vendor == null)
        {
            return Result.Failure<VendorDto>(Error.NotFound("Vendor.NotFound", "Vendor not found."));
        }

        if (request.Approved)
        {
            vendor.Status = VendorStatus.Approved;
            vendor.ApprovedAtUtc = DateTime.UtcNow;
            vendor.RejectionReason = null;
            if (request.CommissionRate.HasValue)
            {
                vendor.CommissionRate = request.CommissionRate.Value;
            }
        }
        else
        {
            vendor.Status = VendorStatus.Rejected;
            vendor.RejectionReason = request.RejectionReason ?? "Vendor application was declined.";
        }

        vendor.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(ct);

        return Result.Success(MapToDto(vendor));
    }

    private static string GenerateSlug(string text)
    {
        var str = text.ToLowerInvariant();
        str = Regex.Replace(str, @"[^a-z0-9\s-]", "");
        str = Regex.Replace(str, @"\s+", " ").Trim();
        str = Regex.Replace(str, @"\s", "-");
        return str;
    }

    private static VendorDto MapToDto(Vendor v) => new(
        v.Id,
        v.OwnerUserId,
        v.BusinessName,
        v.Slug,
        v.Description,
        v.ContactEmail,
        v.ContactPhone,
        v.Status,
        v.CommissionRate,
        v.RejectionReason,
        v.ApprovedAtUtc,
        v.CreatedAtUtc);
}
