using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

namespace ModsDude.Server.Api.Admin;

/// <summary>
/// Basic auth against <see cref="AdminOptions"/>, for the pages run by whoever operates the server
/// rather than by the app's users: a browser cannot send the bearer token the API expects, and an
/// operator should not need a ModsDude account.
/// </summary>
public class AdminAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<AdminOptions> adminOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Admin";
    public const string PolicyName = "Admin";


    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var admin = adminOptions.Value;

        if (!admin.IsConfigured || !TryReadCredentials(Request.Headers.Authorization, out var username, out var password))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // Both compared, in constant time and as hashes of equal length, so the time taken says
        // nothing about which half was wrong or how long either one is.
        var usernameMatches = HashEquals(username, admin.Username);
        var passwordMatches = HashEquals(password, admin.Password);

        if (!(usernameMatches & passwordMatches))
        {
            return Task.FromResult(AuthenticateResult.Fail("Wrong admin username or password."));
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, admin.Username)], SchemeName);

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // The challenge is what makes a browser ask for the credentials rather than show a blank page.
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Basic realm=\"ModsDude admin\", charset=\"UTF-8\"";

        return Task.CompletedTask;
    }


    private static bool TryReadCredentials(string? header, out string username, out string password)
    {
        username = "";
        password = "";

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
            // A malformed header is the same as no header: the browser is challenged again.
            return false;
        }

        var separator = decoded.IndexOf(':');
        if (separator < 0)
        {
            return false;
        }

        username = decoded[..separator];
        password = decoded[(separator + 1)..];
        return true;
    }

    private static bool HashEquals(string given, string expected)
    {
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(given)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
    }
}
