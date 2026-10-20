using System.Net;
using System.Net.Http.Json;
using SaasStarter.Api.Dtos;
using Xunit;

namespace SaasStarter.Tests;

/// <summary>
/// End-to-end auth + project flows over HTTP, including the flagship promise
/// of this starter: tenant isolation that holds without the controllers
/// writing a single tenant WHERE clause.
/// </summary>
public class AuthAndProjectsTests : IClassFixture<SaasApiFactory>
{
    private readonly SaasApiFactory _factory;

    public AuthAndProjectsTests(SaasApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Plans_Are_Public_And_Seeded()
    {
        var client = _factory.CreateClient();

        var plans = await client.GetFromJsonAsync<List<PlanDto>>("/api/plans");

        Assert.NotNull(plans);
        Assert.Equal(3, plans!.Count);
        Assert.Contains(plans, p => p.Code == "free");
        Assert.Contains(plans, p => p.Code == "pro");
        Assert.Contains(plans, p => p.Code == "team");
    }

    [Fact]
    public async Task Register_CreateProject_ListProject_RoundTrips()
    {
        var (client, _) = await TestAuth.RegisterWorkspaceAsync(_factory);

        var create = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest
        {
            Name = "Launch Plan",
            Description = "Ship the thing."
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var project = await create.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);
        Assert.Equal("Launch Plan", project!.Name);

        var list = await client.GetFromJsonAsync<List<ProjectDto>>("/api/projects");
        Assert.NotNull(list);
        Assert.Contains(list!, p => p.Id == project.Id);
    }

    [Fact]
    public async Task Tenant_Cannot_See_Or_Fetch_Another_Tenants_Project()
    {
        var (clientA, _) = await TestAuth.RegisterWorkspaceAsync(_factory, "Tenant A");
        var create = await clientA.PostAsJsonAsync("/api/projects", new CreateProjectRequest
        {
            Name = "A's secret project"
        });
        var projectA = await create.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(projectA);

        var (clientB, _) = await TestAuth.RegisterWorkspaceAsync(_factory, "Tenant B");

        var listB = await clientB.GetFromJsonAsync<List<ProjectDto>>("/api/projects");
        Assert.NotNull(listB);
        Assert.DoesNotContain(listB!, p => p.Id == projectA!.Id);

        var fetch = await clientB.GetAsync($"/api/projects/{projectA.Id}");
        Assert.Equal(HttpStatusCode.NotFound, fetch.StatusCode);
    }

    [Fact]
    public async Task Demo_Tenants_Are_Isolated_From_Each_Other()
    {
        // Seeded demo data: Acme must never see Globex's "Secret Project".
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "demo@acme.test",
            Password = "Demo123$"
        });
        login.EnsureSuccessStatusCode();
        var session = await login.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session!.Token);

        var projects = await client.GetFromJsonAsync<List<ProjectDto>>("/api/projects");
        Assert.NotNull(projects);
        Assert.Contains(projects!, p => p.Name == "Website Redesign");
        Assert.DoesNotContain(projects!, p => p.Name == "Secret Project");
    }

    [Fact]
    public async Task Login_With_Wrong_Password_Returns_401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "demo@acme.test",
            Password = "WrongPass1$"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Projects_Require_Authentication()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/projects");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Free_Plan_Project_Quota_Is_Enforced()
    {
        var (client, _) = await TestAuth.RegisterWorkspaceAsync(_factory, "Quota Workspace");

        // Free plan allows 3 projects.
        for (var i = 1; i <= 3; i++)
        {
            var ok = await client.PostAsJsonAsync("/api/projects",
                new CreateProjectRequest { Name = $"Project {i}" });
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        }

        var fourth = await client.PostAsJsonAsync("/api/projects",
            new CreateProjectRequest { Name = "One too many" });
        Assert.Equal(HttpStatusCode.Conflict, fourth.StatusCode);
    }
}
