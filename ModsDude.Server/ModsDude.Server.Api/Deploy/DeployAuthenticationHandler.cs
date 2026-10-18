using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

namespace ModsDude.Server.Api.Deploy;

/// <summary>
/// <c>Authorization: Deploy &lt;token&gt;</c> against <see cref="DeployOptions"/>, for CI. A scheme of
/// its own rather than Bearer, so the user token handler never sees the deploy token.
/// </summary>
public class DeployAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<DeployOptions> deployOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Deploy";
    public const string PolicyName = "Deploy";


    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var deploy = deployOptions.Value;

        if (!deploy.IsConfigured
            || !AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header)
            || !string.Equals(header.Scheme, SchemeName, StringComparison.OrdinalIgnoreCase)
            || header.Parameter is null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // Compared in constant time and as hashes of equal length, so the time taken says nothing
        // about the token.
        var matches = CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(header.Parameter)),
            SHA256.HashData(Encoding.UTF8.GetBytes(deploy.Token)));

        if (!matches)
        {
            return Task.FromResult(AuthenticateResult.Fail("Wrong deploy token."));
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, SchemeName)], SchemeName);

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
