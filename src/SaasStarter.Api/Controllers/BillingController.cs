using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SaasStarter.Api.Dtos;
using SaasStarter.Infrastructure.Billing;
using SaasStarter.Infrastructure.Data;
using SaasStarter.Infrastructure.Tenancy;
using Stripe;

namespace SaasStarter.Api.Controllers;

/// <summary>
/// Billing: status, Stripe Checkout for upgrades, the Billing Portal for
/// self-serve management, and the webhook Stripe calls when money moves.
/// Test mode only — configure Stripe:SecretKey with an sk_test_ key.
/// </summary>
[ApiController]
[Route("api/billing")]
[Authorize]
public class BillingController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IBillingService _billing;
    private readonly StripeOptions _stripeOptions;
    private readonly StripeWebhookHandler _webhookHandler;

    public BillingController(
        AppDbContext db,
        ITenantContext tenant,
        IBillingService billing,
        IOptions<StripeOptions> stripeOptions,
        StripeWebhookHandler webhookHandler)
    {
        _db = db;
        _tenant = tenant;
        _billing = billing;
        _stripeOptions = stripeOptions.Value;
        _webhookHandler = webhookHandler;
    }

    [HttpGet("status")]
    public async Task<ActionResult<BillingStatusDto>> GetStatus()
    {
        var tenant = await _db.Tenants.Include(t => t.Plan)
            .FirstAsync(t => t.Id == _tenant.TenantId);
        return Ok(new BillingStatusDto(
            tenant.Plan.Code, tenant.Plan.Name, tenant.SubscriptionStatus.ToString(),
            tenant.TrialEndsAtUtc, tenant.CurrentPeriodEndUtc, _billing.IsConfigured));
    }

    [HttpPost("checkout")]
    [Authorize(Policy = "TenantAdmin")]
    public async Task<ActionResult<CheckoutResponse>> CreateCheckout(CheckoutRequest request)
    {
        var plan = await _db.Plans.FirstOrDefaultAsync(p => p.Code == request.PlanCode);
        if (plan is null || plan.MonthlyPriceCents == 0)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid plan",
                detail: $"'{request.PlanCode}' is not a paid plan you can check out.");
        }

        if (!_stripeOptions.Prices.TryGetValue(plan.Code, out var priceId)
            || string.IsNullOrWhiteSpace(priceId))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Plan not mapped to a Stripe price",
                detail: $"Set Stripe:Prices:{plan.Code} to the price_ id from your Stripe dashboard (test mode).");
        }

        var tenant = await _db.Tenants.FirstAsync(t => t.Id == _tenant.TenantId);
        var owner = await _db.Users.FirstAsync(u => u.TenantId == tenant.Id
            && u.Role == Domain.TenantRole.Owner);

        try
        {
            tenant.StripeCustomerId = await _billing.EnsureCustomerAsync(tenant, owner.Email!);
            await _db.SaveChangesAsync();

            var url = await _billing.CreateCheckoutSessionAsync(tenant, plan.Code, priceId);
            return Ok(new CheckoutResponse(url));
        }
        catch (BillingNotConfiguredException ex)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Billing not configured", detail: ex.Message);
        }
    }

    [HttpPost("portal")]
    [Authorize(Policy = "TenantAdmin")]
    public async Task<ActionResult<CheckoutResponse>> CreatePortal()
    {
        var tenant = await _db.Tenants.FirstAsync(t => t.Id == _tenant.TenantId);
        if (string.IsNullOrEmpty(tenant.StripeCustomerId))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "No billing account yet",
                detail: "Subscribe to a paid plan first — the portal opens once a Stripe customer exists.");
        }

        try
        {
            var url = await _billing.CreatePortalSessionAsync(tenant);
            return Ok(new CheckoutResponse(url));
        }
        catch (BillingNotConfiguredException ex)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Billing not configured", detail: ex.Message);
        }
    }

    /// <summary>
    /// Stripe calls this — it is intentionally anonymous and protected by the
    /// webhook signing secret instead (see StripeWebhookHandler). Point the
    /// Stripe CLI at it locally: stripe listen --forward-to localhost:8080/api/billing/webhook
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook()
    {
        var json = await new StreamReader(Request.Body).ReadToEndAsync();
        try
        {
            var result = await _webhookHandler.HandleAsync(
                json, Request.Headers["Stripe-Signature"].ToString());
            return Ok(new { received = true, handled = result.Handled, type = result.EventType });
        }
        catch (StripeException ex)
        {
            // Bad signature or malformed event: tell Stripe not to retry junk.
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid webhook", detail: ex.Message);
        }
    }
}
