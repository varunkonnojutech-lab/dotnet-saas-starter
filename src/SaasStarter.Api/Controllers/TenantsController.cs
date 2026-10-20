using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaasStarter.Api.Dtos;
using SaasStarter.Domain;
using SaasStarter.Infrastructure.Data;
using SaasStarter.Infrastructure.Tenancy;

namespace SaasStarter.Api.Controllers;

/// <summary>
/// Tenant (workspace) administration: settings, members and roles.
/// NOTE: ApplicationUser deliberately has NO EF query filter (Identity must
/// be able to find users at login, before a tenant is known), so every query
/// here filters by TenantId explicitly. Tenant-owned domain entities like
/// Project get the automatic filter instead — see AppDbContext.
/// </summary>
[ApiController]
[Route("api/tenants")]
[Authorize]
public class TenantsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITenantContext _tenant;

    public TenantsController(AppDbContext db, UserManager<ApplicationUser> userManager, ITenantContext tenant)
    {
        _db = db;
        _userManager = userManager;
        _tenant = tenant;
    }

    [HttpGet("current")]
    public async Task<ActionResult<TenantDto>> GetCurrent()
    {
        var tenant = await LoadTenantAsync();
        return tenant is null ? NotFound() : Ok(TenantDto.From(tenant));
    }

    [HttpPatch("current")]
    [Authorize(Policy = "TenantAdmin")]
    public async Task<ActionResult<TenantDto>> UpdateCurrent(UpdateTenantRequest request)
    {
        var tenant = await LoadTenantAsync();
        if (tenant is null)
        {
            return NotFound();
        }

        tenant.Name = request.Name;
        await _db.SaveChangesAsync();
        return Ok(TenantDto.From(tenant));
    }

    [HttpGet("members")]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> GetMembers()
    {
        var members = await _db.Users
            .Where(u => u.TenantId == _tenant.TenantId)
            .OrderBy(u => u.CreatedAtUtc)
            .ToListAsync();
        return Ok(members.Select(UserDto.From).ToList());
    }

    [HttpPost("members")]
    [Authorize(Policy = "TenantAdmin")]
    public async Task<ActionResult<UserDto>> CreateMember(CreateMemberRequest request)
    {
        if (!Enum.TryParse<TenantRole>(request.Role, ignoreCase: true, out var role)
            || role == TenantRole.Owner)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid role",
                detail: "Role must be 'Admin' or 'Member'. A tenant has exactly one Owner.");
        }

        var tenant = await LoadTenantAsync();
        if (tenant is null)
        {
            return NotFound();
        }

        // Plan quota: MaxMembers of -1 means unlimited.
        if (tenant.Plan.MaxMembers >= 0)
        {
            var memberCount = await _db.Users.CountAsync(u => u.TenantId == _tenant.TenantId);
            if (memberCount >= tenant.Plan.MaxMembers)
            {
                return Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Plan limit reached",
                    detail: $"The {tenant.Plan.Name} plan allows {tenant.Plan.MaxMembers} members. Upgrade to add more.");
            }
        }

        if (await _userManager.FindByEmailAsync(request.Email) is not null)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Email already registered",
                detail: $"An account with email '{request.Email}' already exists.");
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            FullName = request.FullName,
            Email = request.Email,
            UserName = request.Email,
            TenantId = _tenant.TenantId,
            Role = role
        };

        var created = await _userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            return ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    ["password"] = created.Errors.Select(e => e.Description).ToArray()
                }));
        }

        return CreatedAtAction(nameof(GetMembers), null, UserDto.From(user));
    }

    [HttpDelete("members/{userId:guid}")]
    [Authorize(Policy = "TenantAdmin")]
    public async Task<IActionResult> DeleteMember(Guid userId)
    {
        if (userId == User.GetUserId())
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Cannot remove yourself",
                detail: "You can't remove your own membership.");
        }

        var member = await _db.Users.FirstOrDefaultAsync(
            u => u.Id == userId && u.TenantId == _tenant.TenantId);
        if (member is null)
        {
            return NotFound();
        }

        if (member.Role == TenantRole.Owner)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Cannot remove the owner",
                detail: "The workspace owner can't be removed.");
        }

        await _userManager.DeleteAsync(member);
        return NoContent();
    }

    private async Task<Tenant?> LoadTenantAsync() =>
        await _db.Tenants.Include(t => t.Plan)
            .FirstOrDefaultAsync(t => t.Id == _tenant.TenantId);
}
