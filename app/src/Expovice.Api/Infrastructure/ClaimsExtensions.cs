using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Expovice.Api.Infrastructure;

public static class ClaimsExtensions
{
    /// <summary>The signed-in user's id from the access token's "sub" claim.</summary>
    public static Guid UserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id)
            ? id
            : throw new InvalidOperationException("Authenticated principal has no valid 'sub' claim.");
}
