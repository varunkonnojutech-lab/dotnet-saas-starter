using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace SaasStarter.Api;

/// <summary>Small helpers for reading the claims JwtTokenService issues.</summary>
public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                  ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }
}
