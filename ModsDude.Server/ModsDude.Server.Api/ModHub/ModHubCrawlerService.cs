using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.ModHub;
using ModsDude.Server.ModHub;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.ModHub;

/// <summary>
/// Keeps <see cref="ModHubMod"/> in step with ModHub, so a client can ask what ModHub has without
/// asking ModHub. The server knows nothing else about any game; this is the one exception, and it is
/// kept to this folder and its own project.
/// </summary>
/// <remarks>
/// <para>
/// Every tick does up to three things per game, in this order:
/// </para>
/// <list type="bullet">
/// <item><b>Sweep</b> - read every page of the "latest" listing and fetch each mod not yet stored. The
/// first one is the backfill, a couple of hours at one request a second; after that one runs every
/// <see cref="ModHubOptions.SweepInterval"/> to pick up anything a poll missed. Resumable: the next page
/// is persisted after each one.</item>
/// <item><b>Poll</b> - read the top of the listing down to where the previous poll left off, and fetch
/// everything above that; see <see cref="ModHubListingChanges"/>.</item>
/// <item><b>Refresh</b> - read again the few mods read longest ago. This catches what the listing's
/// order cannot show, and it is how a mod ModHub removed gets deleted.</item>
/// </list>
/// <para>
/// A run that meets a page it cannot read stops, and changes nothing it has not already finished: a
/// redesigned site has to show up as an error in the log, never as every mod gone.
/// </para>
/// </remarks>
public class ModHubCrawlerService(
    IServiceScopeFactory scopeFactory,
    IModHubSite site,
    ITimeService timeService,
    IOptions<ModHubOptions> options,
    ILogger<ModHubCrawlerService> logger)
    : BackgroundService
{
    /// <summary>Long enough to stay off the startup path, like the other maintenance jobs.</summary>
    private static readonly TimeSpan _startupDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How many mod pages in a row may fail to parse before the run is taken to be reading a site that
    /// has changed, rather than one odd mod.
    /// </summary>
    private const int _unreadableModsInARow = 5;


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.Enabled is false)
        {
            logger.LogInformation("The ModHub crawler is disabled; stored ModHub data will not change.");
            return;
        }

        if (options.Value.Games.Count == 0)
        {
            logger.LogWarning("The ModHub crawler has no games configured (ModHub:Games); nothing will be crawled.");
            return;
        }

        try
        {
            await Task.Delay(_startupDelay, stoppingToken);

            while (true)
            {
                foreach (var game in options.Value.Games.Distinct())
                {
                    await RunGuardedAsync(game, stoppingToken);
                }

                await Task.Delay(options.Value.PollInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down. A sweep in progress resumes from its persisted page.
        }
    }


    private async Task RunGuardedAsync(string game, CancellationToken cancellationToken)
    {
        try
        {
            await RunAsync(game, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ModHubUnavailableException exception)
        {
            logger.LogWarning(exception, "ModHub was unavailable while crawling {Game}; trying again next poll.", game);
        }
        catch (ModHubUnreadableException exception)
        {
            // The one to notice: nothing will be picked up until the parser is brought up to date.
            logger.LogError(exception, "A ModHub page for {Game} could not be read. ModHub may have changed its pages.", game);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Crawling ModHub for {Game} failed.", game);
        }
    }

    private async Task RunAsync(string game, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var state = await dbContext.ModHubCrawlStates.FindAsync([game], cancellationToken);

        if (state is null)
        {
            state = new ModHubCrawlState { Game = game };
            dbContext.ModHubCrawlStates.Add(state);
        }

        var run = new Run(game, dbContext, state);

        if (state.SweepNextPage is not null
            || state.SweepCompletedAt is null
            || timeService.Now() - state.SweepCompletedAt >= options.Value.SweepInterval)
        {
            await SweepAsync(run, cancellationToken);
        }

        await PollAsync(run, cancellationToken);
        await RefreshAsync(run, cancellationToken);
    }

    private async Task SweepAsync(Run run, CancellationToken cancellationToken)
    {
        var page = run.State.SweepNextPage ?? 0;
        var fetched = 0;

        if (page == 0)
        {
            logger.LogInformation("Starting a sweep of ModHub's {Game} listing.", run.Game);
        }

        while (true)
        {
            var ids = await ReadListingPageAsync(run.Game, page, cancellationToken);

            // The first sweep has no previous poll to take a head from. Recording the top as it stood
            // when the sweep began is what lets the first poll catch what changed during a long one.
            if (page == 0 && run.State.HeadModIds.Count == 0)
            {
                run.State.HeadModIds = [.. ids];
            }

            if (ids.Count == 0)
            {
                run.State.SweepNextPage = null;
                run.State.SweepCompletedAt = timeService.Now();
                await run.SaveAsync(cancellationToken);

                logger.LogInformation(
                    "Swept ModHub's {Game} listing: {Pages} pages, {Fetched} mods fetched this run.",
                    run.Game, page, fetched);

                return;
            }

            var known = await run.DbContext.ModHubMods.GetKnownIdsAsync(run.Game, ids, cancellationToken);

            foreach (var id in ids.Where(x => known.Contains(x) is false).Distinct())
            {
                await FetchAsync(run, id, cancellationToken);
                fetched++;
            }

            run.State.SweepNextPage = ++page;
            await run.SaveAsync(cancellationToken);
        }
    }

    private async Task PollAsync(Run run, CancellationToken cancellationToken)
    {
        var listing = new List<int>();
        List<int>? firstPage = null;
        int? boundary = null;

        for (var page = 0; page < options.Value.PollPageLimit; page++)
        {
            var ids = await ReadListingPageAsync(run.Game, page, cancellationToken);

            firstPage ??= [.. ids];
            listing.AddRange(ids);
            boundary = ModHubListingChanges.FindBoundary(listing, run.State.HeadModIds);

            if (boundary is not null || ids.Count == 0)
            {
                break;
            }
        }

        if (boundary is null)
        {
            logger.LogInformation(
                "A ModHub poll of {Game} did not find where the previous one ended; taking all {Count} mods it read as changed.",
                run.Game, listing.Count);
        }

        var changed = listing.Take(boundary ?? listing.Count).Distinct().ToList();

        foreach (var id in changed)
        {
            await FetchAsync(run, id, cancellationToken);
        }

        run.State.HeadModIds = firstPage ?? [];
        run.State.PolledAt = timeService.Now();
        await run.SaveAsync(cancellationToken);

        if (changed.Count > 0)
        {
            logger.LogInformation("A ModHub poll of {Game} found {Count} new or updated mods.", run.Game, changed.Count);
        }
    }

    private async Task RefreshAsync(Run run, CancellationToken cancellationToken)
    {
        var stalest = await run.DbContext.ModHubMods.GetStalestIdsAsync(run.Game, options.Value.RefreshPerPoll, cancellationToken);

        foreach (var id in stalest)
        {
            await FetchAsync(run, id, cancellationToken);
        }

        await run.SaveAsync(cancellationToken);
    }

    /// <summary>
    /// A listing page, where the first page being empty is taken to be a site that has changed rather
    /// than a listing with nothing in it - ModHub always has mods.
    /// </summary>
    private async Task<IReadOnlyList<int>> ReadListingPageAsync(string game, int page, CancellationToken cancellationToken)
    {
        var ids = await site.GetLatestPage(game, page, cancellationToken);

        return page == 0 && ids.Count == 0
            ? throw new ModHubUnreadableException($"The first page of the {game} listing has no mods on it.")
            : ids;
    }

    /// <summary>
    /// Reads one mod's page and stores what it says - or deletes the mod, where ModHub no longer has it.
    /// Not saved here; the caller saves at its own checkpoints.
    /// </summary>
    private async Task FetchAsync(Run run, int modHubId, CancellationToken cancellationToken)
    {
        ModHubModDetails? details;

        try
        {
            details = await site.GetMod(run.Game, modHubId, cancellationToken);
            run.UnreadableInARow = 0;
        }
        catch (ModHubUnreadableException exception) when (++run.UnreadableInARow < _unreadableModsInARow)
        {
            logger.LogWarning(exception, "ModHub's page for {Game} mod {ModHubId} could not be read; skipping it.", run.Game, modHubId);
            return;
        }

        var stored = await run.DbContext.ModHubMods.FindAsync([run.Game, modHubId], cancellationToken);

        if (details is null)
        {
            if (stored is not null)
            {
                run.DbContext.ModHubMods.Remove(stored);
                logger.LogInformation("{Game} mod {ModHubId} ({FileName}) is no longer on ModHub.", run.Game, modHubId, stored.FileName);
            }

            return;
        }

        if (stored is null)
        {
            run.DbContext.ModHubMods.Add(ModHubMod.Create(run.Game, modHubId, details, timeService.Now()));
        }
        else
        {
            stored.Apply(details, timeService.Now());
        }
    }


    private sealed class Run(string game, ApplicationDbContext dbContext, ModHubCrawlState state)
    {
        public string Game { get; } = game;
        public ApplicationDbContext DbContext { get; } = dbContext;
        public ModHubCrawlState State { get; } = state;
        public int UnreadableInARow { get; set; }


        /// <summary>
        /// Saves, then lets go of the mods saved: a backfill would otherwise hold all six thousand of them
        /// in the change tracker and check every one on every save.
        /// </summary>
        public async Task SaveAsync(CancellationToken cancellationToken)
        {
            await DbContext.SaveChangesAsync(cancellationToken);

            foreach (var entry in DbContext.ChangeTracker.Entries<ModHubMod>().ToList())
            {
                entry.State = EntityState.Detached;
            }
        }
    }
}
