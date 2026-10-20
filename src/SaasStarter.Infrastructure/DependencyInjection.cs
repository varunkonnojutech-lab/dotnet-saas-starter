using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SaasStarter.Domain;
using SaasStarter.Infrastructure.Billing;
using SaasStarter.Infrastructure.Data;
using SaasStarter.Infrastructure.Email;
using SaasStarter.Infrastructure.Identity;
using SaasStarter.Infrastructure.Jobs;
using SaasStarter.Infrastructure.Tenancy;

namespace SaasStarter.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContext, TenantContext>();

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.Configure<StripeOptions>(configuration.GetSection(StripeOptions.SectionName));

        // ---- Data: SQLite by default (zero setup). Set Database:Provider to
        // "SqlServer" (Azure SQL in production) or "InMemory" (tests). ----
        var provider = configuration["Database:Provider"] ?? "Sqlite";
        var connectionString = configuration.GetConnectionString("DefaultConnection")
                               ?? "Data Source=saas-starter.db";
        services.AddDbContext<AppDbContext>(options =>
        {
            switch (provider)
            {
                case "SqlServer":
                    options.UseSqlServer(connectionString);
                    break;
                case "InMemory":
                    options.UseInMemoryDatabase(configuration["Database:Name"] ?? "saas-starter");
                    break;
                default:
                    options.UseSqlite(connectionString);
                    break;
            }
        });

        // ---- Identity: users live in our DbContext; JWTs are issued by
        // JwtTokenService. (Microsoft Entra ID can replace local passwords —
        // see the AzureAd section in the API's Program.cs.) ----
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<AppDbContext>();

        services.AddScoped<IJwtTokenService, JwtTokenService>();

        // ---- Email: provider selected by config, default Console. ----
        var emailProvider = configuration[$"{EmailOptions.SectionName}:Provider"] ?? "Console";
        switch (emailProvider)
        {
            case "Smtp":
                services.AddScoped<IEmailSender, SmtpEmailSender>();
                break;
            case "SendGrid":
                services.AddHttpClient<IEmailSender, SendGridEmailSender>();
                break;
            default:
                services.AddScoped<IEmailSender, ConsoleEmailSender>();
                break;
        }

        // ---- Billing: real Stripe when a (test-mode) key is configured. ----
        var stripeKey = configuration[$"{StripeOptions.SectionName}:SecretKey"];
        if (!string.IsNullOrWhiteSpace(stripeKey))
        {
            services.AddScoped<IBillingService, StripeBillingService>();
        }
        else
        {
            services.AddScoped<IBillingService, UnconfiguredBillingService>();
        }
        services.AddScoped<StripeWebhookHandler>();

        // ---- Background jobs: in-process channel queue. ----
        services.AddSingleton<IBackgroundJobQueue, BackgroundJobQueue>();
        services.AddHostedService<QueuedHostedService>();

        return services;
    }
}
