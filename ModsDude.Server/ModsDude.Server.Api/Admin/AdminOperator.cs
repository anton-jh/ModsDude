using System.Security.Claims;

namespace ModsDude.Server.Api.Admin;

public static class AdminOperator
{
    /// <summary>Who is signed in to the admin pages, for the log of what they did.</summary>
    public static string OperatorName(this ClaimsPrincipal principal)
    {
        return principal.Identity?.Name ?? "unknown";
    }
}
