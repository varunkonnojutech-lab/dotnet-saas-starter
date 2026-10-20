namespace SaasStarter.Domain;

/// <summary>
/// A tenant is one customer workspace: its own users, its own projects and
/// its own subscription. All tenant data hangs off this row, and every
/// tenant-owned entity carries <see cref="Id"/> as its TenantId.
/// </summary>
public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>URL-safe unique identifier, e.g. "acme-inc". Used by the X-Tenant-Slug header.</summary>
    public string Slug { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // ---- Subscription state (kept in sync by the Stripe webhook handler) ----

    public int PlanId { get; set; }
    public Plan Plan { get; set; } = null!;

    public SubscriptionStatus SubscriptionStatus { get; set; } = SubscriptionStatus.Trialing;

    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }

    public DateTime? TrialEndsAtUtc { get; set; }
    public DateTime? CurrentPeriodEndUtc { get; set; }

    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
    public ICollection<Project> Projects { get; set; } = new List<Project>();
}
