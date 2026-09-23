using Hangfire.Dashboard;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace ModsDude.Server.Api.Maintenance;

/// <summary>
/// Lets the Hangfire dashboard in with the one username and password in configuration. The dashboard
/// is for whoever runs the server rather than for the app's users, so it deliberately does not ride
/// on their sign-in: a browser cannot send the bearer token the API expects, and an operator should
/// not need a ModsDude account to look at a failed job.
/// </summary>
public class BasicAuthDashboardFilter(string username, string password) : IDashboardAuthorizationFilter
{
    private readonly byte[] _username = Encoding.UTF8.GetBytes(username);
    private readonly byte[] _password = Encoding.UTF8.GetBytes(password);


    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        if (IsAuthorized(httpContext.Request.Headers.Authorization))
        {
            return true;
        }

        // Hangfire answers 401 for a refusal like this one; the challenge is what makes a browser
        // ask for the credentials rather than show a blank page.
        httpContext.Response.Headers.WWWAuthenticate = "Basic realm=\"Hangfire\", charset=\"UTF-8\"";
        return false;
    }


    private bool IsAuthorized(string? header)
    {
        if (!AuthenticationHeaderValue.TryParse(header, out var parsed)
            || !string.Equals(parsed.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
            || parsed.Parameter is null)
        {
            return false;
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(parsed.Parameter));
        }
        catch (FormatException)
        {
            return false;
        }

        var separator = decoded.IndexOf(':');
        if (separator < 0)
        {
            return false;
        }

        // Both compared, in constant time, whatever the first comparison said - so the time taken
        // says nothing about which half was wrong.
        var usernameMatches = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(decoded[..separator]), _username);
        var passwordMatches = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(decoded[(separator + 1)..]), _password);

        return usernameMatches & passwordMatches;
    }
}
