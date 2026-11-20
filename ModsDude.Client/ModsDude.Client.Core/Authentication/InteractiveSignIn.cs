using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace ModsDude.Client.Core.Authentication;

/// <summary>The authorization code flow with PKCE, through the system browser and a loopback redirect.</summary>
public sealed class InteractiveSignIn(
    OidcClientOptions options,
    IBrowserLauncher browser,
    ITokenEndpointClient tokens,
    ILogger<InteractiveSignIn> logger)
    : IInteractiveSignIn
{
    public async Task<TokenSet?> SignInAsync(SignInPrompt prompt, CancellationToken cancellationToken)
    {
        using var listener = LoopbackCallbackListener.Start();

        var state = Base64Url.Encode(RandomNumberGenerator.GetBytes(16));
        var verifier = Pkce.CreateVerifier();

        browser.Open(AuthorizeUrl(options, listener.RedirectUri, state, Pkce.ChallengeFor(verifier), prompt));

        var callback = await listener.WaitForCallbackAsync(state, cancellationToken);

        if (callback.TryGetValue("error", out var error) || !callback.TryGetValue("code", out var code))
        {
            logger.LogWarning(
                "The sign-in page came back without a code: {Error} {Description}",
                error ?? "no code",
                callback.GetValueOrDefault("error_description"));

            return null;
        }

        return await tokens.ExchangeCodeAsync(code, verifier, listener.RedirectUri, cancellationToken);
    }


    /// <remarks>
    /// Always prompts: signing in is how a different user is chosen, so a browser still signed in as
    /// somebody must not be let through without asking.
    /// </remarks>
    public static Uri AuthorizeUrl(
        OidcClientOptions options,
        Uri redirectUri,
        string state,
        string codeChallenge,
        SignInPrompt prompt)
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("client_id", options.ClientId),
            new("response_type", "code"),
            new("redirect_uri", redirectUri.ToString()),
            new("scope", OidcClientOptions.Scopes),
            new("state", state),
            new("code_challenge", codeChallenge),
            new("code_challenge_method", Pkce.Method),
            new("prompt", prompt is SignInPrompt.CreateAccount ? "create" : "login")
        };

        var query = string.Join("&", parameters.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));

        return new Uri($"{new Uri(options.Issuer, OidcClientOptions.AuthorizePath)}?{query}");
    }
}
