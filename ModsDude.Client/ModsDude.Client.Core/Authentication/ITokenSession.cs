namespace ModsDude.Client.Core.Authentication;

public interface ITokenSession
{
    /// <summary>Who the current tokens are for. Null until a sign-in has been adopted or resumed, and never null again.</summary>
    SignedInIdentity? Identity { get; }

    /// <summary>
    /// Signs in with the refresh token kept from the last run, if there is one the server still takes.
    /// </summary>
    /// <returns>False where somebody has to sign in.</returns>
    Task<bool> TryResumeAsync(CancellationToken cancellationToken);

    /// <summary>A current access token, refreshed first when it is about to run out.</summary>
    /// <exception cref="SignInRequiredException">Nobody is signed in, or the server refused the refresh token.</exception>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);

    /// <summary>Makes these the tokens, replacing whatever account was signed in.</summary>
    Task AdoptAsync(TokenSet tokens, CancellationToken cancellationToken);
}
