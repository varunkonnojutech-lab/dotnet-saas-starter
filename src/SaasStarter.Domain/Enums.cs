namespace SaasStarter.Domain;

/// <summary>Role a user holds inside their tenant (workspace).</summary>
public enum TenantRole
{
    Member = 0,
    Admin = 1,
    Owner = 2
}

/// <summary>Mirrors the billing lifecycle of a tenant's subscription.</summary>
public enum SubscriptionStatus
{
    Trialing = 0,
    Active = 1,
    PastDue = 2,
    Canceled = 3,
    Incomplete = 4
}
