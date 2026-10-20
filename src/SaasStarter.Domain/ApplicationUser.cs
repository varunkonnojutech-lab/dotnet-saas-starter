using Microsoft.AspNetCore.Identity;

namespace SaasStarter.Domain;

/// <summary>
/// Application user. Every user belongs to exactly one tenant and holds a
/// <see cref="TenantRole"/> inside it. The tenant id and role are copied into
/// the JWT at login, so the API never has to look them up per request.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = string.Empty;

    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public TenantRole Role { get; set; } = TenantRole.Member;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
