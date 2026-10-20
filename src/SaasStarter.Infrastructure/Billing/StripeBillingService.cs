using Microsoft.Extensions.Options;
using SaasStarter.Domain;
using Stripe;
using Stripe.Checkout;

namespace SaasStarter.Infrastructure.Billing;

public class StripeOptions
{
    public const string SectionName = "Stripe";

    /// <summary>Test-mode secret key (sk_test_...). Empty = billing disabled (see UnconfiguredBillingService).</summary>
    public string SecretKey { get; set; } = string.Empty;

    public string PublishableKey { get; set; } = string.Empty;

    /// <summary>Webhook signing secret (whsec_...) from `stripe listen` or the dashboard.</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    public string SuccessUrl { get; set; } = "http://localhost:5173/billing?checkout=success";
    public string CancelUrl { get; set; } = "http://localhost:5173/billing?checkout=cancelled";
    public string PortalReturnUrl { get; set; } = "http://localhost:5173/billing";

    /// <summary>Plan code -> Stripe Price id (price_...), created in the Stripe dashboard (test mode).</summary>
    public Dictionary<string, string> Prices { get; set; } = new();
}

public class BillingNotConfiguredException : InvalidOperationException
{
    public BillingNotConfiguredException()
        : base("Stripe billing is not configured. Set Stripe:SecretKey to a test-mode key " +
               "(sk_test_...) and map plan prices under Stripe:Prices to enable checkout.")
    {
    }
}

public record SubscriptionSnapshot(string Status, string? PriceId, DateTime? CurrentPeriodEndUtc);

public interface IBillingService
{
    bool IsConfigured { get; }

    /// <summary>Creates the Stripe customer for a tenant (idempotent: returns the existing id).</summary>
    Task<string> EnsureCustomerAsync(Tenant tenant, string ownerEmail, CancellationToken cancellationToken = default);

    /// <summary>Creates a subscription Checkout session and returns its hosted URL.</summary>
    Task<string> CreateCheckoutSessionAsync(
        Tenant tenant, string planCode, string priceId, CancellationToken cancellationToken = default);

    /// <summary>Creates a Billing Portal session (manage card, invoices, cancel) and returns its URL.</summary>
    Task<string> CreatePortalSessionAsync(Tenant tenant, CancellationToken cancellationToken = default);

    Task<SubscriptionSnapshot?> GetSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Stripe billing on the official Stripe.net SDK, in test mode. No keys live
/// in the repo: everything comes from the Stripe configuration section
/// (environment variables Stripe__SecretKey etc. in real deployments).
/// </summary>
public class StripeBillingService : IBillingService
{
    private readonly StripeOptions _options;

    public StripeBillingService(IOptions<StripeOptions> options)
    {
        _options = options.Value;
        StripeConfiguration.ApiKey = _options.SecretKey;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.SecretKey);

    public async Task<string> EnsureCustomerAsync(
        Tenant tenant, string ownerEmail, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(tenant.StripeCustomerId))
        {
            return tenant.StripeCustomerId;
        }

        var service = new CustomerService();
        var customer = await service.CreateAsync(new CustomerCreateOptions
        {
            Email = ownerEmail,
            Name = tenant.Name,
            Metadata = new Dictionary<string, string> { ["tenantId"] = tenant.Id.ToString() }
        }, cancellationToken: cancellationToken);

        return customer.Id;
    }

    public async Task<string> CreateCheckoutSessionAsync(
        Tenant tenant, string planCode, string priceId, CancellationToken cancellationToken = default)
    {
        var metadata = new Dictionary<string, string>
        {
            ["tenantId"] = tenant.Id.ToString(),
            ["planCode"] = planCode
        };

        var service = new SessionService();
        var session = await service.CreateAsync(new SessionCreateOptions
        {
            Mode = "subscription",
            Customer = tenant.StripeCustomerId,
            SuccessUrl = _options.SuccessUrl,
            CancelUrl = _options.CancelUrl,
            LineItems = new List<SessionLineItemOptions>
            {
                new() { Price = priceId, Quantity = 1 }
            },
            Metadata = metadata,
            SubscriptionData = new SessionSubscriptionDataOptions { Metadata = metadata }
        }, cancellationToken: cancellationToken);

        return session.Url;
    }

    public async Task<string> CreatePortalSessionAsync(
        Tenant tenant, CancellationToken cancellationToken = default)
    {
        var service = new Stripe.BillingPortal.SessionService();
        var session = await service.CreateAsync(new Stripe.BillingPortal.SessionCreateOptions
        {
            Customer = tenant.StripeCustomerId,
            ReturnUrl = _options.PortalReturnUrl
        }, cancellationToken: cancellationToken);

        return session.Url;
    }

    public async Task<SubscriptionSnapshot?> GetSubscriptionAsync(
        string subscriptionId, CancellationToken cancellationToken = default)
    {
        var service = new SubscriptionService();
        var subscription = await service.GetAsync(subscriptionId, cancellationToken: cancellationToken);
        if (subscription is null)
        {
            return null;
        }

        var item = subscription.Items?.Data.FirstOrDefault();
        return new SubscriptionSnapshot(
            subscription.Status,
            item?.Price?.Id,
            item?.CurrentPeriodEnd);
    }
}

/// <summary>Used when no Stripe key is configured: the app runs fully, billing calls fail loudly and clearly.</summary>
public class UnconfiguredBillingService : IBillingService
{
    public bool IsConfigured => false;

    public Task<string> EnsureCustomerAsync(Tenant tenant, string ownerEmail, CancellationToken cancellationToken = default)
        => throw new BillingNotConfiguredException();

    public Task<string> CreateCheckoutSessionAsync(
        Tenant tenant, string planCode, string priceId, CancellationToken cancellationToken = default)
        => throw new BillingNotConfiguredException();

    public Task<string> CreatePortalSessionAsync(Tenant tenant, CancellationToken cancellationToken = default)
        => throw new BillingNotConfiguredException();

    public Task<SubscriptionSnapshot?> GetSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken = default)
        => throw new BillingNotConfiguredException();
}
