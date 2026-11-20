namespace ModsDude.Client.Core.Authentication;

public interface ITokenEndpointClient
{
    /// <exception cref="TokenGrantRejectedException">The code was refused.</exception>
    Task<TokenSet> ExchangeCodeAsync(string code, string codeVerifier, Uri redirectUri, CancellationToken cancellationToken);

    /// <summary>The refresh token handed in is used up; the returned set carries the one that replaces it.</summary>
    /// <exception cref="TokenGrantRejectedException">The refresh token was refused.</exception>
    Task<TokenSet> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
}
