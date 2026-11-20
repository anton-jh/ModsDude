using ModsDude.Server.Application.Services;
using OpenIddict.Abstractions;

namespace ModsDude.Server.Persistence.Accounts;

/// <summary>
/// Deletes the tokens and authorizations that can no longer be used. Idempotent: a second run, or one
/// beside another, finds less or nothing to delete.
/// </summary>
public class TokenPruner(
    IOpenIddictTokenManager tokens,
    IOpenIddictAuthorizationManager authorizations,
    ITimeService timeService)
    : ITokenPruner
{
    /// <summary>
    /// Kept this long after they were created, so a token's history is still there to look at for a
    /// while after it stopped working. Longer than any refresh token lives.
    /// </summary>
    public static readonly TimeSpan MinimumAge = TimeSpan.FromDays(45);


    public async Task<TokenPruneResult> PruneAsync(CancellationToken cancellationToken)
    {
        var threshold = new DateTimeOffset(DateTime.SpecifyKind(timeService.Now(), DateTimeKind.Utc)) - MinimumAge;

        var prunedTokens = await tokens.PruneAsync(threshold, cancellationToken);
        var prunedAuthorizations = await authorizations.PruneAsync(threshold, cancellationToken);

        return new TokenPruneResult(prunedTokens, prunedAuthorizations);
    }
}
