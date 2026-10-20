using Microsoft.EntityFrameworkCore;
using SaasStarter.Infrastructure.Data;
using SaasStarter.Infrastructure.Tenancy;

namespace SaasStarter.Api.Middleware;

/// <summary>
/// Resolves the tenant for the request.
///
/// Primary source: the <c>tenant_id</c> claim in the JWT (via ITenantContext).
/// Secondary source: an <c>X-Tenant-Slug</c> header, for integrations that
/// address a workspace by slug. The header can only ever CONFIRM the claim's
/// tenant — if it names a different tenant the request is rejected, so the
/// header is an addressing convenience, never an escalation path.
/// </summary>
public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AppDbContext db, ITenantContext tenantContext)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var slug = context.Request.Headers["X-Tenant-Slug"].ToString();
            if (!string.IsNullOrWhiteSpace(slug))
            {
                var tenant = await db.Tenants.AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Slug == slug);
                if (tenant is null)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsJsonAsync(
                        new { error = $"Unknown tenant slug '{slug}'." });
                    return;
                }

                if (tenantContext.HasTenant && tenant.Id != tenantContext.TenantId)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(
                        new { error = "The X-Tenant-Slug header does not match your account's tenant." });
                    return;
                }

                tenantContext.SetTenant(tenant.Id);
            }
        }

        await _next(context);
    }
}
