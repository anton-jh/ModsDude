namespace ModsDude.Server.Persistence.Accounts;

public interface ITokenPruner
{
    Task<TokenPruneResult> PruneAsync(CancellationToken cancellationToken);
}

public sealed record TokenPruneResult(long Tokens, long Authorizations);
