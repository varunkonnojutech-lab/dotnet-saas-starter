using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using SaasStarter.Api.Dtos;
using Xunit;

namespace SaasStarter.Tests;

/// <summary>
/// Billing flows that don't need Stripe's servers: the admin-only policy,
/// and the webhook -> plan upgrade round trip using a fabricated
/// (unsigned, dev-mode) checkout.session.completed event.
/// </summary>
public class BillingTests : IClassFixture<SaasApiFactory>
{
    private readonly SaasApiFactory _factory;

    public BillingTests(SaasApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Member_Cannot_Start_Checkout_But_Owner_Gets_A_Clear_Answer()
    {
        var (ownerClient, session) = await TestAuth.RegisterWorkspaceAsync(_factory, "Billing Workspace");

        // Owner adds a plain member.
        var memberEmail = TestAuth.UniqueEmail("member");
        var addMember = await ownerClient.PostAsJsonAsync("/api/tenants/members", new CreateMemberRequest
        {
            FullName = "Team Member",
            Email = memberEmail,
            Password = "Passw0rd$",
            Role = "Member"
        });
        Assert.Equal(HttpStatusCode.Created, addMember.StatusCode);

        var memberClient = _factory.CreateClient();
        var login = await memberClient.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = memberEmail,
            Password = "Passw0rd$"
        });
        var memberSession = await login.Content.ReadFromJsonAsync<AuthResponse>();
        memberClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", memberSession!.Token);

        // Members are blocked by the TenantAdmin policy before anything else.
        var memberCheckout = await memberClient.PostAsJsonAsync("/api/billing/checkout",
            new CheckoutRequest { PlanCode = "pro" });
        Assert.Equal(HttpStatusCode.Forbidden, memberCheckout.StatusCode);

        // The owner passes the policy; with no Stripe key configured the API
        // answers 400 (price not mapped) or 503 (billing not configured) —
        // both are explicit, documented states, never a 500.
        var ownerCheckout = await ownerClient.PostAsJsonAsync("/api/billing/checkout",
            new CheckoutRequest { PlanCode = "pro" });
        Assert.True(
            ownerCheckout.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.ServiceUnavailable,
            $"Unexpected status {(int)ownerCheckout.StatusCode}");
        _ = session;
    }

    [Fact]
    public async Task Checkout_Completed_Webhook_Upgrades_The_Tenant_Plan()
    {
        var (client, session) = await TestAuth.RegisterWorkspaceAsync(_factory, "Webhook Workspace");
        var tenantId = session.Tenant.Id;

        var before = await client.GetFromJsonAsync<BillingStatusDto>("/api/billing/status");
        Assert.NotNull(before);
        Assert.Equal("free", before!.PlanCode);

        // Fabricated Stripe event. No webhook secret is configured in tests,
        // so the handler parses (dev mode) instead of verifying a signature.
        var eventJson = $$"""
        {
          "id": "evt_test_123",
          "object": "event",
          "api_version": "2025-04-30.basil",
          "created": 1735689600,
          "type": "checkout.session.completed",
          "livemode": false,
          "pending_webhooks": 1,
          "request": { "id": "req_test_123", "idempotency_key": null },
          "data": {
            "object": {
              "id": "cs_test_123",
              "object": "checkout.session",
              "customer": "cus_test_123",
              "subscription": "sub_test_123",
              "payment_status": "paid",
              "status": "complete",
              "mode": "subscription",
              "metadata": { "tenantId": "{{tenantId}}", "planCode": "pro" }
            }
          }
        }
        """;

        var anonymous = _factory.CreateClient();
        var webhook = await anonymous.PostAsync(
            "/api/billing/webhook",
            new StringContent(eventJson, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, webhook.StatusCode);

        var after = await client.GetFromJsonAsync<BillingStatusDto>("/api/billing/status");
        Assert.NotNull(after);
        Assert.Equal("pro", after!.PlanCode);
        Assert.Equal("Active", after.SubscriptionStatus);
    }
}
