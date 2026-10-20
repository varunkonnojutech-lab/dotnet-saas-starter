using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SaasStarter.Infrastructure.Data;

namespace SaasStarter.Tests;

/// <summary>
/// Boots the real API in-process with the DbContext swapped to an EF Core
/// InMemory database (one unique database per factory instance). The swap
/// happens by replacing the DbContext registration — the documented
/// WebApplicationFactory pattern — so tests can never touch the dev SQLite
/// file, even when test classes run in parallel.
/// </summary>
public class SaasApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = "saas-tests-" + Guid.NewGuid().ToString("N");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            var descriptors = services.Where(d =>
                d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                || d.ServiceType == typeof(AppDbContext)).ToList();
            foreach (var descriptor in descriptors)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<AppDbContext>((_, options) =>
                options.UseInMemoryDatabase(_databaseName));
        });
    }
}
