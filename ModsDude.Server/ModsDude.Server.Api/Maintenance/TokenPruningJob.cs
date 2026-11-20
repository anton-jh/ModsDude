using Hangfire;
using ModsDude.Server.Persistence.Accounts;

namespace ModsDude.Server.Api.Maintenance;

/// <summary>
/// The daily token cleanup as Hangfire runs it. The work lives in <see cref="ITokenPruner"/>.
/// </summary>
public class TokenPruningJob(
    ITokenPruner pruner,
    ILogger<TokenPruningJob> logger)
{
    public const string JobId = "token-pruning";

    /// <summary>Daily at 04:30 UTC, after blob reclamation.</summary>
    private const string _cron = "30 4 * * *";


    public static void Register(IRecurringJobManager recurringJobs)
    {
        recurringJobs.AddOrUpdate<TokenPruningJob>(JobId, x => x.PruneAsync(CancellationToken.None), _cron);
    }


    // Deleting what is already gone deletes nothing, so a retry or a second run by hand is harmless.
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public async Task PruneAsync(CancellationToken cancellationToken)
    {
        var pruned = await pruner.PruneAsync(cancellationToken);

        logger.LogInformation("Pruned {TokenCount} tokens and {AuthorizationCount} authorizations.", pruned.Tokens, pruned.Authorizations);
    }
}
