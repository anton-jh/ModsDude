using OpenIddict.Abstractions;

namespace ModsDude.Server.Api.Accounts;

public class AccountSessions(
    IOpenIddictAuthorizationManager authorizations,
    IOpenIddictTokenManager tokens,
    ILogger<AccountSessions> logger)
    : IAccountSessions
{
    public async Task RevokeAllAsync(string accountId, CancellationToken cancellationToken)
    {
        var revokedAuthorizations = await authorizations.RevokeBySubjectAsync(accountId, cancellationToken);
        var revokedTokens = await tokens.RevokeBySubjectAsync(accountId, cancellationToken);

        logger.LogInformation(
            "Revoked {AuthorizationCount} authorizations and {TokenCount} tokens of account {AccountId}.",
            revokedAuthorizations, revokedTokens, accountId);
    }
}
