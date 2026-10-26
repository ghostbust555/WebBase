using System.Security.Claims;

namespace WebBase.Api.Infrastructure;

public static class ClaimsExtensions
{
    /// <summary>
    /// The signed-in user's id, from their token. Only call this from an
    /// [Authorize] endpoint, where a valid token is guaranteed.
    /// </summary>
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id)
            ? id
            : throw new InvalidOperationException("No user id on this request - is the endpoint missing [Authorize]?");
    }
}
