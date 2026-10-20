using Microsoft.AspNetCore.Http;

namespace SaasStarter.Infrastructure.Tenancy;

/// <summary>
/// The tenant the current unit of work belongs to. Resolved once per request
/// from the JWT <c>tenant_id</c> claim (or the <c>X-Tenant-Slug</c> header,
/// validated against the claim — see TenantResolutionMiddleware in the API).
/// Background jobs and tests can set it explicitly.
/// </summary>
public interface ITenantContext
{
    Guid TenantId { get; }
    bool HasTenant { get; }
    void SetTenant(Guid tenantId);
}

public class TenantContext : ITenantContext
{
    /// <summary>Claim type written into the JWT by JwtTokenService.</summary>
    public const string TenantClaimType = "tenant_id";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private Guid? _override;

    public TenantContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid TenantId
    {
        get
        {
            if (_override is not null)
            {
                return _override.Value;
            }

            var claim = _httpContextAccessor.HttpContext?.User.FindFirst(TenantClaimType)?.Value;
            return Guid.TryParse(claim, out var tenantId) ? tenantId : Guid.Empty;
        }
    }

    public bool HasTenant => TenantId != Guid.Empty;

    public void SetTenant(Guid tenantId) => _override = tenantId;
}
