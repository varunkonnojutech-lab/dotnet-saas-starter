using System.ComponentModel.DataAnnotations;
using SaasStarter.Domain;

namespace SaasStarter.Api.Dtos;

// ---- Auth ----

public class RegisterRequest
{
    [Required, MaxLength(200)]
    public string WorkspaceName { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8), MaxLength(100)]
    public string Password { get; set; } = string.Empty;
}

public class LoginRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public record UserDto(Guid Id, string FullName, string Email, string Role)
{
    public static UserDto From(ApplicationUser user) =>
        new(user.Id, user.FullName, user.Email ?? string.Empty, user.Role.ToString());
}

public record TenantDto(
    Guid Id, string Name, string Slug, string PlanCode, string PlanName,
    string SubscriptionStatus, DateTime? TrialEndsAtUtc, DateTime? CurrentPeriodEndUtc)
{
    public static TenantDto From(Tenant tenant) =>
        new(tenant.Id, tenant.Name, tenant.Slug,
            tenant.Plan.Code, tenant.Plan.Name,
            tenant.SubscriptionStatus.ToString(),
            tenant.TrialEndsAtUtc, tenant.CurrentPeriodEndUtc);
}

public record AuthResponse(string Token, DateTime ExpiresAtUtc, UserDto User, TenantDto Tenant);

public record SessionResponse(UserDto User, TenantDto Tenant);

// ---- Plans ----

public record PlanDto(
    string Code, string Name, string Description, int MonthlyPriceCents,
    int MaxProjects, int MaxMembers)
{
    public static PlanDto From(Plan plan) =>
        new(plan.Code, plan.Name, plan.Description, plan.MonthlyPriceCents,
            plan.MaxProjects, plan.MaxMembers);
}

// ---- Tenant administration ----

public class UpdateTenantRequest
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;
}

public class CreateMemberRequest
{
    [Required, MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8), MaxLength(100)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Admin | Member (a tenant has exactly one Owner — its creator).</summary>
    [Required]
    public string Role { get; set; } = "Member";
}

// ---- Projects ----

public record ProjectDto(
    Guid Id, string Name, string? Description, Guid CreatedByUserId,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc)
{
    public static ProjectDto From(Project project) =>
        new(project.Id, project.Name, project.Description, project.CreatedByUserId,
            project.CreatedAtUtc, project.UpdatedAtUtc);
}

public class CreateProjectRequest
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }
}

public class UpdateProjectRequest
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }
}

// ---- Billing ----

public record BillingStatusDto(
    string PlanCode, string PlanName, string SubscriptionStatus,
    DateTime? TrialEndsAtUtc, DateTime? CurrentPeriodEndUtc, bool StripeConfigured);

public class CheckoutRequest
{
    [Required]
    public string PlanCode { get; set; } = string.Empty;
}

public record CheckoutResponse(string Url);
