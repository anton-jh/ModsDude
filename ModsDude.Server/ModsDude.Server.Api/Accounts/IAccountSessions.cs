namespace ModsDude.Server.Api.Accounts;

public interface IAccountSessions
{
    /// <summary>
    /// Revokes every refresh token the account holds, so each device has to sign in again once its
    /// access token runs out. For after the password changes.
    /// </summary>
    Task RevokeAllAsync(string accountId, CancellationToken cancellationToken);
}
