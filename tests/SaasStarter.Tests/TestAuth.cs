using System.Net.Http.Headers;
using System.Net.Http.Json;
using SaasStarter.Api.Dtos;
using Xunit;

namespace SaasStarter.Tests;

/// <summary>Shared arrange helpers: register a fresh workspace and get an authorized client.</summary>
public static class TestAuth
{
    public static string UniqueEmail(string prefix = "user") =>
        $"{prefix}-{Guid.NewGuid():N}@example.test";

    public static async Task<(HttpClient Client, AuthResponse Session)> RegisterWorkspaceAsync(
        SaasApiFactory factory, string workspaceName = "Test Workspace")
    {
        var client = factory.CreateClient();
        var email = UniqueEmail("owner");
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            WorkspaceName = workspaceName,
            FullName = "Test Owner",
            Email = email,
            Password = "Passw0rd$"
        });
        response.EnsureSuccessStatusCode();

        var session = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(session);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session!.Token);
        return (client, session);
    }
}
