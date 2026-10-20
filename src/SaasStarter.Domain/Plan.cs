namespace SaasStarter.Domain;

/// <summary>
/// A billing plan. Plans are platform-wide reference data (seeded), not
/// tenant-owned. Quotas are enforced by the API; <c>MaxProjects</c> /
/// <c>MaxMembers</c> of <c>-1</c> means "unlimited".
/// </summary>
public class Plan
{
    public int Id { get; set; }

    /// <summary>Stable machine code used by the API and Stripe metadata: free | pro | team.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Display price, in cents, per month. 0 for the free plan.</summary>
    public int MonthlyPriceCents { get; set; }

    public int MaxProjects { get; set; }
    public int MaxMembers { get; set; }

    public ICollection<Tenant> Tenants { get; set; } = new List<Tenant>();
}
