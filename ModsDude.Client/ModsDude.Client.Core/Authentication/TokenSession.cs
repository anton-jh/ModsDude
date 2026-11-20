using Microsoft.Extensions.Logging;

namespace ModsDude.Client.Core.Authentication;

/// <summary>
/// The signed-in account's tokens. One refresh at a time: every refresh token works once, so two
/// requests refreshing at the same moment would leave one of them holding a used-up token.
/// </summary>
/// <remarks>
/// A refresh is never cancelled once it has started. The server replaces the refresh token the moment
/// it answers, and a refresh dropped after that would leave only the used-up token, which the server
/// treats as stolen.
/// </remarks>
public sealed class TokenSession(
    ITokenEndpointClient tokens,
    ITokenStore store,
    TimeProvider time,
    ILogger<TokenSession> logger)
    : ITokenSession, IDisposable
{
    /// <summary>Refreshed this long before it runs out, so a token is never sent just as it expires.</summary>
    public static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _gate = new(1, 1);

    private TokenSet? _current;

    /// <summary>Set once the server refused the refresh token, so it is not asked again until somebody signs in.</summary>
    private bool _isRejected;


    public SignedInIdentity? Identity => Volatile.Read(ref _current)?.Identity;


    public async Task<bool> TryResumeAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_current is not null)
            {
                return !_isRejected;
            }

            if (await store.LoadRefreshTokenAsync(cancellationToken) is not string refreshToken)
            {
                return false;
            }

            try
            {
                await ReplaceAsync(await tokens.RefreshAsync(refreshToken, CancellationToken.None));

                return true;
            }
            catch (TokenGrantRejectedException exception)
            {
                logger.LogInformation(exception, "The kept refresh token was refused, so somebody has to sign in.");
                await store.ClearAsync(CancellationToken.None);

                return false;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _current) is TokenSet fresh && !Volatile.Read(ref _isRejected) && !IsExpiring(fresh))
        {
            return fresh.AccessToken;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_current is not TokenSet current || _isRejected)
            {
                throw new SignInRequiredException();
            }

            // Another request may have refreshed while this one waited.
            if (!IsExpiring(current))
            {
                return current.AccessToken;
            }

            try
            {
                var refreshed = await tokens.RefreshAsync(current.RefreshToken, CancellationToken.None);
                await ReplaceAsync(refreshed);

                return refreshed.AccessToken;
            }
            catch (TokenGrantRejectedException exception)
            {
                logger.LogInformation(exception, "The refresh token was refused, so somebody has to sign in again.");
                Volatile.Write(ref _isRejected, true);
                await store.ClearAsync(CancellationToken.None);

                throw new SignInRequiredException(exception);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AdoptAsync(TokenSet tokens, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            await ReplaceAsync(tokens);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
    }


    /// <summary>
    /// Used from now on even where it cannot be kept on disk: the token it replaces is used up either
    /// way, and this one at least serves until the app closes.
    /// </summary>
    private async Task ReplaceAsync(TokenSet tokens)
    {
        Volatile.Write(ref _current, tokens);
        Volatile.Write(ref _isRejected, false);

        try
        {
            await store.SaveRefreshTokenAsync(tokens.RefreshToken, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            logger.LogWarning(exception, "Could not keep the refresh token, so the next start will ask to sign in.");
        }
    }

    private bool IsExpiring(TokenSet tokens) => tokens.AccessTokenExpiresAt - time.GetUtcNow() <= RefreshMargin;
}
