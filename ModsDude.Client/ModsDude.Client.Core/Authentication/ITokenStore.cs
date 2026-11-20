namespace ModsDude.Client.Core.Authentication;

/// <summary>Where the refresh token is kept between runs, protected for the Windows user alone.</summary>
public interface ITokenStore
{
    Task<string?> LoadRefreshTokenAsync(CancellationToken cancellationToken);

    Task SaveRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}
