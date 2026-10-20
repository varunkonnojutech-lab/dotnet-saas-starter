using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaasStarter.Api.Dtos;
using SaasStarter.Domain;
using SaasStarter.Infrastructure.Data;
using SaasStarter.Infrastructure.Identity;
using SaasStarter.Infrastructure.Jobs;
using SaasStarter.Infrastructure.Tenancy;

namespace SaasStarter.Api.Controllers;

/// <summary>
/// Local accounts. Registering creates a brand-new tenant (workspace) with
/// the caller as its Owner on a free 14-day trial — the standard SaaS
/// self-serve signup shape.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _db;
    private readonly IJwtTokenService _jwt;
    private readonly IBackgroundJobQueue _jobs;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        AppDbContext db,
        IJwtTokenService jwt,
        IBackgroundJobQueue jobs)
    {
        _userManager = userManager;
        _db = db;
        _jwt = jwt;
        _jobs = jobs;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        if (await _userManager.FindByEmailAsync(request.Email) is not null)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Email already registered",
                detail: $"An account with email '{request.Email}' already exists. Try logging in.");
        }

        var freePlan = await _db.Plans.FirstAsync(p => p.Code == "free");
        var tenant = new Tenant
        {
            Name = request.WorkspaceName,
            Slug = await UniqueSlugAsync(request.WorkspaceName),
            PlanId = freePlan.Id,
            SubscriptionStatus = SubscriptionStatus.Trialing,
            TrialEndsAtUtc = DateTime.UtcNow.AddDays(14)
        };
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            FullName = request.FullName,
            Email = request.Email,
            UserName = request.Email,
            TenantId = tenant.Id,
            Role = TenantRole.Owner
        };

        var created = await _userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            // Roll back the tenant so a failed signup leaves nothing behind.
            _db.Tenants.Remove(tenant);
            await _db.SaveChangesAsync();
            return ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    ["password"] = created.Errors.Select(e => e.Description).ToArray()
                }));
        }

        // Fire-and-forget through the background job queue: signup never
        // waits on an email provider.
        await _jobs.EnqueueAsync(new SendEmailJob(
            user.Email!,
            $"Welcome to {tenant.Name} on SaaS Starter",
            $"<p>Hi {user.FullName},</p><p>Your workspace <b>{tenant.Name}</b> is ready. " +
            "Your free trial runs for 14 days — no card required.</p>"));

        tenant.Plan = freePlan;
        var (token, expires) = _jwt.CreateToken(user);
        return Ok(new AuthResponse(token, expires, UserDto.From(user), TenantDto.From(tenant)));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null || !await _userManager.CheckPasswordAsync(user, request.Password))
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid credentials",
                detail: "Email or password is incorrect.");
        }

        var tenant = await _db.Tenants.Include(t => t.Plan)
            .FirstAsync(t => t.Id == user.TenantId);
        var (token, expires) = _jwt.CreateToken(user);
        return Ok(new AuthResponse(token, expires, UserDto.From(user), TenantDto.From(tenant)));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<SessionResponse>> Me()
    {
        var user = await _userManager.FindByIdAsync(User.GetUserId().ToString());
        if (user is null)
        {
            return Unauthorized();
        }

        var tenant = await _db.Tenants.Include(t => t.Plan)
            .FirstAsync(t => t.Id == user.TenantId);
        return Ok(new SessionResponse(UserDto.From(user), TenantDto.From(tenant)));
    }

    private async Task<string> UniqueSlugAsync(string workspaceName)
    {
        var baseSlug = SlugGenerator.FromName(workspaceName);
        var slug = baseSlug;
        var suffix = 2;
        while (await _db.Tenants.AnyAsync(t => t.Slug == slug))
        {
            slug = $"{baseSlug}-{suffix++}";
        }

        return slug;
    }
}
