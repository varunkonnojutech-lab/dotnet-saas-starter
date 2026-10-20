using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaasStarter.Domain;
using SaasStarter.Infrastructure.Data;
using Stripe;
using Stripe.Checkout;

namespace SaasStarter.Infrastructure.Billing;

public record WebhookResult(string EventType, bool Handled, Guid? TenantId);

/// <summary>
/// Applies Stripe webhook events to tenant subscription state. This handler
/// is the ONLY writer of Tenant.SubscriptionStatus after signup — the API
/// never trusts a browser redirect to mean "the customer paid".
///
/// Signature verification: when Stripe:WebhookSecret is set, events are
/// verified with EventUtility.ConstructEvent. When it is empty (local dev
/// only) the payload is parsed unverified and a warning is logged — never
/// run production that way.
/// </summary>
public class StripeWebhookHandler
{
    private readonly AppDbContext _db;
    private readonly IBillingService _billing;
    private readonly StripeOptions _options;
    private readonly ILogger<StripeWebhookHandler> _logger;

    public StripeWebhookHandler(
        AppDbContext db,
        IBillingService billing,
        IOptions<StripeOptions> options,
        ILogger<StripeWebhookHandler> logger)
    {
        _db = db;
        _billing = billing;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<WebhookResult> HandleAsync(
        string json, string? signatureHeader, CancellationToken cancellationToken = default)
    {
        var stripeEvent = ParseEvent(json, signatureHeader);

        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
                return await OnCheckoutCompletedAsync(stripeEvent, cancellationToken);
            case "customer.subscription.updated":
                return await OnSubscriptionUpdatedAsync(stripeEvent, cancellationToken);
            case "customer.subscription.deleted":
                return await OnSubscriptionDeletedAsync(stripeEvent, cancellationToken);
            case "invoice.payment_failed":
                return await OnPaymentFailedAsync(stripeEvent, cancellationToken);
            default:
                _logger.LogInformation("Ignoring unhandled Stripe event {EventType}", stripeEvent.Type);
                return new WebhookResult(stripeEvent.Type, false, null);
        }
    }

    private Event ParseEvent(string json, string? signatureHeader)
    {
        if (!string.IsNullOrWhiteSpace(_options.WebhookSecret))
        {
            // Throws StripeException on a bad signature -> the controller turns it into a 400.
            return EventUtility.ConstructEvent(
                json, signatureHeader, _options.WebhookSecret, throwOnApiVersionMismatch: false);
        }

        _logger.LogWarning(
            "Stripe webhook received WITHOUT signature verification (Stripe:WebhookSecret is empty). Dev mode only.");
        return EventUtility.ParseEvent(json, throwOnApiVersionMismatch: false);
    }

    private async Task<WebhookResult> OnCheckoutCompletedAsync(Event stripeEvent, CancellationToken ct)
    {
        var session = (Session)stripeEvent.Data.Object;
        if (!TryGetTenantId(session.Metadata, out var tenantId))
        {
            _logger.LogWarning("checkout.session.completed without tenantId metadata");
            return new WebhookResult(stripeEvent.Type, false, null);
        }

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null)
        {
            return new WebhookResult(stripeEvent.Type, false, tenantId);
        }

        tenant.StripeCustomerId = session.CustomerId ?? tenant.StripeCustomerId;
        tenant.StripeSubscriptionId = session.SubscriptionId ?? tenant.StripeSubscriptionId;
        tenant.SubscriptionStatus = SubscriptionStatus.Active;
        tenant.TrialEndsAtUtc = null;

        var planCode = ResolvePlanCode(session.Metadata);
        if (planCode is null && tenant.StripeSubscriptionId is not null && _billing.IsConfigured)
        {
            var snapshot = await _billing.GetSubscriptionAsync(tenant.StripeSubscriptionId, ct);
            planCode = PlanCodeForPrice(snapshot?.PriceId);
            tenant.CurrentPeriodEndUtc = snapshot?.CurrentPeriodEndUtc;
        }

        await ApplyPlanAsync(tenant, planCode, ct);
        await _db.SaveChangesAsync(ct);
        return new WebhookResult(stripeEvent.Type, true, tenant.Id);
    }

    private async Task<WebhookResult> OnSubscriptionUpdatedAsync(Event stripeEvent, CancellationToken ct)
    {
        var subscription = (Subscription)stripeEvent.Data.Object;
        var tenant = await FindTenantAsync(subscription.Id, subscription.CustomerId, ct);
        if (tenant is null)
        {
            return new WebhookResult(stripeEvent.Type, false, null);
        }

        tenant.SubscriptionStatus = MapStatus(subscription.Status);
        var item = subscription.Items?.Data.FirstOrDefault();
        tenant.CurrentPeriodEndUtc = item?.CurrentPeriodEnd;
        await ApplyPlanAsync(tenant, PlanCodeForPrice(item?.Price?.Id), ct);

        await _db.SaveChangesAsync(ct);
        return new WebhookResult(stripeEvent.Type, true, tenant.Id);
    }

    private async Task<WebhookResult> OnSubscriptionDeletedAsync(Event stripeEvent, CancellationToken ct)
    {
        var subscription = (Subscription)stripeEvent.Data.Object;
        var tenant = await FindTenantAsync(subscription.Id, subscription.CustomerId, ct);
        if (tenant is null)
        {
            return new WebhookResult(stripeEvent.Type, false, null);
        }

        tenant.SubscriptionStatus = SubscriptionStatus.Canceled;
        tenant.CurrentPeriodEndUtc = null;
        await ApplyPlanAsync(tenant, "free", ct);

        await _db.SaveChangesAsync(ct);
        return new WebhookResult(stripeEvent.Type, true, tenant.Id);
    }

    private async Task<WebhookResult> OnPaymentFailedAsync(Event stripeEvent, CancellationToken ct)
    {
        var invoice = (Invoice)stripeEvent.Data.Object;
        var tenant = await _db.Tenants.FirstOrDefaultAsync(
            t => t.StripeCustomerId == invoice.CustomerId, ct);
        if (tenant is null)
        {
            return new WebhookResult(stripeEvent.Type, false, null);
        }

        tenant.SubscriptionStatus = SubscriptionStatus.PastDue;
        await _db.SaveChangesAsync(ct);
        _logger.LogWarning("Tenant {TenantId} marked PastDue after invoice payment failure", tenant.Id);
        return new WebhookResult(stripeEvent.Type, true, tenant.Id);
    }

    private async Task<Tenant?> FindTenantAsync(string subscriptionId, string? customerId, CancellationToken ct)
    {
        return await _db.Tenants.FirstOrDefaultAsync(t => t.StripeSubscriptionId == subscriptionId, ct)
               ?? (customerId is null
                   ? null
                   : await _db.Tenants.FirstOrDefaultAsync(t => t.StripeCustomerId == customerId, ct));
    }

    private async Task ApplyPlanAsync(Tenant tenant, string? planCode, CancellationToken ct)
    {
        if (planCode is null)
        {
            return;
        }

        var plan = await _db.Plans.FirstOrDefaultAsync(p => p.Code == planCode, ct);
        if (plan is not null)
        {
            tenant.PlanId = plan.Id;
        }
    }

    private string? PlanCodeForPrice(string? priceId)
    {
        if (priceId is null)
        {
            return null;
        }

        return _options.Prices.FirstOrDefault(kv => kv.Value == priceId).Key;
    }

    private static string? ResolvePlanCode(IDictionary<string, string>? metadata)
    {
        return metadata is not null && metadata.TryGetValue("planCode", out var code) ? code : null;
    }

    private static bool TryGetTenantId(IDictionary<string, string>? metadata, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        return metadata is not null
               && metadata.TryGetValue("tenantId", out var raw)
               && Guid.TryParse(raw, out tenantId);
    }

    private static SubscriptionStatus MapStatus(string stripeStatus) => stripeStatus switch
    {
        "active" => SubscriptionStatus.Active,
        "trialing" => SubscriptionStatus.Trialing,
        "past_due" => SubscriptionStatus.PastDue,
        "canceled" or "unpaid" => SubscriptionStatus.Canceled,
        _ => SubscriptionStatus.Incomplete
    };
}
