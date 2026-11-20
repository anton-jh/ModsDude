namespace ModsDude.Client.Core.Authentication;

/// <param name="Issuer">The server, which is its own sign-in provider.</param>
public sealed record OidcClientOptions(Uri Issuer, string ClientId)
{
    /// <summary>How the server knows this client; registered on its side under the same id.</summary>
    public const string DesktopClientId = "modsdude-client";

    public const string Scopes = "openid profile offline_access api";

    public const string AuthorizePath = "connect/authorize";
    public const string TokenPath = "connect/token";
    public const string ChangePasswordPath = "account/password";
    public const string ChangeEmailPath = "account/email";
}
