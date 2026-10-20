using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SaasStarter.Domain;
using SaasStarter.Infrastructure.Tenancy;

namespace SaasStarter.Infrastructure.Data;

/// <summary>
/// Seeds plans and a demo workspace so the app is explorable the second it
/// starts. Demo credentials (documented in the README — local/dev only):
/// demo@acme.test / Demo123$ and demo@globex.test / Demo123$.
/// Seeding is idempotent: it only runs when no plans/tenants exist.
/// Production deployments should run EF migrations instead of EnsureCreated
/// and skip demo data (Database:SeedDemo=false).
/// </summary>
public static class DbSeeder
{
    public const string DemoPassword = "Demo123$";

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();

        await db.Database.EnsureCreatedAsync();

        if (!await db.Plans.AnyAsync())
        {
            db.Plans.AddRange(
                new Plan
                {
                    Id = 1, Code = "free", Name = "Free",
                    Description = "For trying things out and side projects.",
                    MonthlyPriceCents = 0, MaxProjects = 3, MaxMembers = 3
                },
                new Plan
                {
                    Id = 2, Code = "pro", Name = "Pro",
                    Description = "For small teams shipping real products.",
                    MonthlyPriceCents = 2900, MaxProjects = 25, MaxMembers = 25
                },
                new Plan
                {
                    Id = 3, Code = "team", Name = "Team",
                    Description = "Unlimited projects and members, priority support.",
                    MonthlyPriceCents = 9900, MaxProjects = -1, MaxMembers = -1
                });
            await db.SaveChangesAsync();
        }

        if (await db.Tenants.AnyAsync())
        {
            return;
        }

        var freePlan = await db.Plans.FirstAsync(p => p.Code == "free");

        var acme = new Tenant
        {
            Name = "Acme Inc",
            Slug = SlugGenerator.FromName("Acme Inc"),
            PlanId = freePlan.Id,
            SubscriptionStatus = SubscriptionStatus.Trialing,
            TrialEndsAtUtc = DateTime.UtcNow.AddDays(14)
        };
        var globex = new Tenant
        {
            Name = "Globex",
            Slug = SlugGenerator.FromName("Globex"),
            PlanId = freePlan.Id,
            SubscriptionStatus = SubscriptionStatus.Trialing,
            TrialEndsAtUtc = DateTime.UtcNow.AddDays(14)
        };
        db.Tenants.AddRange(acme, globex);
        await db.SaveChangesAsync();

        var acmeOwner = await CreateUserAsync(
            userManager, acme, "Demo Owner (Acme)", "demo@acme.test", TenantRole.Owner);
        var globexOwner = await CreateUserAsync(
            userManager, globex, "Demo Owner (Globex)", "demo@globex.test", TenantRole.Owner);

        // IMPORTANT: save between tenant switches — the tenant stamp is read
        // from ITenantContext at SaveChanges time, so one combined save
        // would stamp every project with the LAST tenant set.
        tenantContext.SetTenant(acme.Id);
        db.Projects.AddRange(
            new Project
            {
                Name = "Website Redesign", Description = "Marketing site refresh for Q3.",
                CreatedByUserId = acmeOwner.Id
            },
            new Project
            {
                Name = "Mobile App", Description = "Customer-facing iOS/Android app.",
                CreatedByUserId = acmeOwner.Id
            });
        await db.SaveChangesAsync();

        tenantContext.SetTenant(globex.Id);
        db.Projects.Add(new Project
        {
            Name = "Secret Project",
            Description = "Globex-only. Acme users must never see this row — that's the isolation test.",
            CreatedByUserId = globexOwner.Id
        });

        await db.SaveChangesAsync();
    }

    private static async Task<ApplicationUser> CreateUserAsync(
        UserManager<ApplicationUser> userManager, Tenant tenant,
        string fullName, string email, TenantRole role)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            FullName = fullName,
            Email = email,
            UserName = email,
            TenantId = tenant.Id,
            Role = role,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user, DemoPassword);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Seeding demo user failed: " + string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        return user;
    }
}
