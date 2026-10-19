using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiVendorPlatform.Modules.Identity.Domain;
using MultiVendorPlatform.Modules.Vendors.Domain;
using MultiVendorPlatform.Modules.Vendors.DTOs;
using MultiVendorPlatform.Modules.Vendors.Services;

namespace MultiVendorPlatform.Api.Controllers;

public class VendorsController : BaseApiController
{
    private readonly IVendorService _vendorService;

    public VendorsController(IVendorService vendorService)
    {
        _vendorService = vendorService;
    }

    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(VendorDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RegisterVendor([FromBody] RegisterVendorRequest request, CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var result = await _vendorService.RegisterVendorAsync(userId, request, ct);
        return HandleResult(result);
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(VendorDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVendor(Guid id, CancellationToken ct)
    {
        var result = await _vendorService.GetVendorByIdAsync(id, ct);
        return HandleResult(result);
    }

    [HttpGet("me")]
    [Authorize(Roles = Role.Vendor)]
    [ProducesResponseType(typeof(VendorDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyVendor(CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var result = await _vendorService.GetVendorByOwnerIdAsync(userId, ct);
        return HandleResult(result);
    }

    [HttpGet]
    [Authorize(Roles = Role.Admin)]
    public async Task<IActionResult> ListVendors([FromQuery] VendorStatus? status, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _vendorService.GetVendorsAsync(status, pageNumber, pageSize, ct);
        return HandleResult(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = Role.Vendor)]
    public async Task<IActionResult> UpdateProfile(Guid id, [FromBody] UpdateVendorRequest request, CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var result = await _vendorService.UpdateVendorProfileAsync(id, userId, request, ct);
        return HandleResult(result);
    }

    [HttpPost("{id:guid}/review")]
    [Authorize(Roles = Role.Admin)]
    public async Task<IActionResult> ReviewVendor(Guid id, [FromBody] ApproveVendorRequest request, CancellationToken ct)
    {
        var result = await _vendorService.ReviewVendorAsync(id, request, ct);
        return HandleResult(result);
    }
}
