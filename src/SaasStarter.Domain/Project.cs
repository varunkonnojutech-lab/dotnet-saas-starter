namespace SaasStarter.Domain;

/// <summary>
/// A sample tenant-owned resource. Real SaaS products replace this with
/// their own domain objects; it exists so tenant isolation, quotas and the
/// admin dashboard have something real to work on out of the box.
/// </summary>
public class Project : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
