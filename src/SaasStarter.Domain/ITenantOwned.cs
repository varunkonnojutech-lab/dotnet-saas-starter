namespace SaasStarter.Domain;

/// <summary>
/// Implemented by every entity that belongs to exactly one tenant.
/// <c>AppDbContext</c> applies a global query filter on these entities and
/// stamps <see cref="TenantId"/> on insert, which is what makes cross-tenant
/// data leaks impossible by default instead of by convention.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}
