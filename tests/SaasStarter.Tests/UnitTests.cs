using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SaasStarter.Domain;
using SaasStarter.Infrastructure.Data;
using SaasStarter.Infrastructure.Identity;
using SaasStarter.Infrastructure.Tenancy;
using Xunit;

namespace SaasStarter.Tests;

/// <summary>
/// DbContext-level proof of the two tenancy rules: the query filter hides
/// other tenants' rows, and TenantId is stamped from the context — never
/// trusted from the caller.
/// </summary>
public class TenantIsolationTests
{
    private readonly InMemoryDatabaseRoot _root = new();
    private readonly string _databaseName = "isolation-" + Guid.NewGuid().ToString("N");

    private AppDbContext CreateContext(TenantContext tenantContext)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_databaseName, _root)
            .Options;
        return new AppDbContext(options, tenantContext);
    }

    private static TenantContext ContextFor(Guid tenantId)
    {
        var context = new TenantContext(new HttpContextAccessor());
        context.SetTenant(tenantId);
        return context;
    }

    [Fact]
    public async Task Query_Filter_Hides_Other_Tenants_Projects_And_Stamps_TenantId()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        using (var ctxA = CreateContext(ContextFor(tenantA)))
        {
            ctxA.Tenants.Add(new Tenant { Id = tenantA, Name = "A", Slug = "a", PlanId = 1 });
            ctxA.Tenants.Add(new Tenant { Id = tenantB, Name = "B", Slug = "b", PlanId = 1 });
            await ctxA.SaveChangesAsync();

            var project = new Project { Name = "A project", CreatedByUserId = Guid.NewGuid() };
            Assert.Equal(Guid.Empty, project.TenantId); // caller did not set it
            ctxA.Projects.Add(project);
            await ctxA.SaveChangesAsync();
            Assert.Equal(tenantA, project.TenantId); // context stamped it
        }

        using (var ctxB = CreateContext(ContextFor(tenantB)))
        {
            ctxB.Projects.Add(new Project { Name = "B project", CreatedByUserId = Guid.NewGuid() });
            await ctxB.SaveChangesAsync();

            var visibleToB = await ctxB.Projects.ToListAsync();
            Assert.Single(visibleToB);
            Assert.Equal("B project", visibleToB[0].Name);
        }

        using (var ctxA = CreateContext(ContextFor(tenantA)))
        {
            var visibleToA = await ctxA.Projects.ToListAsync();
            Assert.Single(visibleToA);
            Assert.Equal("A project", visibleToA[0].Name);
        }
    }
}

public class JwtTokenServiceTests
{
    private const string Key = "test-signing-key-that-is-long-enough-0123456789";

    [Fact]
    public void Issued_Token_Carries_Tenant_And_Role_Claims_And_Validates()
    {
        var service = new JwtTokenService(Options.Create(new JwtOptions
        {
            Issuer = "SaasStarter",
            Audience = "SaasStarter",
            Key = Key,
            ExpiryMinutes = 60
        }));

        var tenantId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            FullName = "Test User",
            Email = "user@example.test",
            UserName = "user@example.test",
            TenantId = tenantId,
            Role = TenantRole.Admin
        };

        var (token, _) = service.CreateToken(user);

        var handler = new JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "SaasStarter",
            ValidateAudience = true,
            ValidAudience = "SaasStarter",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
            ValidateLifetime = true
        }, out _);

        // JwtSecurityTokenHandler maps the "role" claim to ClaimTypes.Role on
        // read (the API disables that mapping via MapInboundClaims=false).
        Assert.Equal(tenantId.ToString(), principal.FindFirst("tenant_id")?.Value);
        Assert.Equal("Admin",
            principal.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
            ?? principal.FindFirst("role")?.Value);
        Assert.Equal(user.Id.ToString(),
            principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
    }
}
