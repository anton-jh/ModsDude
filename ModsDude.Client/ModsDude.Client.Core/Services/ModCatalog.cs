using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Imagery;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Services;

/// <summary>
/// One repo's mods, merged: what the enabled sources hold on disk against what the repo has
/// registered. Repo-scoped, and owned by whatever surface is showing it - the import list, the
/// management list and the profile mod editor all need the same three things this does.
/// </summary>
/// <remarks>
/// <para>
/// Scans are cached <em>per source</em> and the merged view is composed on demand, which is what
/// makes a source checkbox usable: toggling one recomposes from memory, and adding a source scans
/// only the new folder. What is cached is the <see cref="Task"/> rather than its result, so a second
/// caller arriving during an in-flight scan joins it instead of starting a second parallel walk over
/// a thousand archives.
/// </para>
/// <para>
/// Nothing here refreshes silently. A stale catalog the user re-triggers is better than one that
/// changes under an interaction, so invalidation is explicit and the rescan actions are public.
/// </para>
/// </remarks>
public sealed class ModCatalog : IDisposable
{
    /// <summary>
    /// How long a caller must survive before it is worth scanning. Dragging down the sidebar builds
    /// and discards one page per item it passes over; holding off briefly means a page nobody
    /// stopped on never touches the disk.
    /// </summary>
    private static readonly TimeSpan _scanDelay = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// The stated target is thousands of registered versions per repo, so the mod list is walked a
    /// page at a time rather than assumed to arrive whole.
    /// </summary>
    private const int _pageSize = 500;

    private readonly Repo _repo;
    private readonly IBaseModAdapter _modAdapter;
    private readonly IModsClient _modsClient;
    private readonly ILogger<ModCatalog> _logger;

    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _lock = new();

    private readonly Dictionary<ModSourceId, Task<SourceScan>> _scans = [];
    private readonly List<ModSource> _adHocSources = [];
    /// <summary>
    /// The sources this page is scanning. Starts empty and is never persisted: opening a page must
    /// not read a disk, and a folder somebody looked in last week is not a standing instruction to
    /// look in it again today. Ad-hoc sources are added on the way in - picking a folder is itself
    /// the act of asking for it to be read, which navigating to a page is not.
    /// </summary>
    private readonly HashSet<ModSourceId> _enabledSources = [];

    /// <summary>
    /// Every source that has been switched on at least once this session, whether or not it still
    /// is. Never persisted, for the same reason <see cref="_enabledSources"/> is not.
    /// </summary>
    /// <remarks>
    /// <b>Switching a source off puts it on standby rather than forgetting it.</b> A chip decides
    /// what a page is <em>looking at</em>, never what this catalog has read - so a source on standby
    /// is kept out of the merged view, stays in <see cref="ModCatalogSnapshot.Known"/>, and is
    /// re-read by a rescan along with everything else. That is what lets a page keep offering a
    /// version whose chip is off without also offering one whose file has since been deleted: the
    /// first is a standby source still reporting it, the second is a rescan no longer doing so.
    /// </remarks>
    private readonly HashSet<ModSourceId> _standbySources = [];

    /// <summary>Registered versions accumulated across delta fetches, keyed by their join key.</summary>
    private readonly Dictionary<ModVersionIdentity, ModDto> _registered = [];

    private Task<IReadOnlyList<ModDto>>? _registeredLoad;
    private DateTime? _registeredThrough;
    private Task<IReadOnlyDictionary<ModVersionIdentity, ModUsage>>? _usageLoad;


    public ModCatalog(
        Repo repo,
        IModsClient modsClient,
        ILogger<ModCatalog> logger)
    {
        _repo = repo;
        _modsClient = modsClient;
        _logger = logger;
        _modAdapter = repo.Adapter.GetBaseCapabilityAdapterFactory<IBaseModAdapter>()?.Invoke()
            ?? throw UserFriendlyException.RepoNoModSupport();
    }


    /// <summary>
    /// Every source currently available, standing ones first. Rebuilt on each call, because the
    /// game list and the settings behind it are live.
    /// </summary>
    public IReadOnlyList<ModSource> GetSources()
    {
        var sources = new List<ModSource>();

        foreach (var game in _repo.Games)
        {
            // One source per target, because looking in one folder of three would report what the
            // other two hold as missing from this machine.
            var targets = ReadTargets(game);

            foreach (var (key, displayName, path) in targets)
            {
                sources.Add(new ModSource(
                    ModSourceId.ForTarget(new ModTargetRef(game.Identity, key)),
                    // The game alone where it has one folder, which is every game the user is
                    // likely to have: naming a folder that has no sibling is noise.
                    targets.Count > 1 && displayName is string folderName
                        ? $"{game.Name} - {folderName}"
                        : game.Name,
                    path,
                    ModSourceKind.Game));
            }
        }

        if (KnownFolders.GetDownloads() is string downloads)
        {
            sources.Add(new ModSource(ModSourceId.Downloads, "Downloads", downloads, ModSourceKind.Downloads));
        }

        lock (_lock)
        {
            sources.AddRange(_adHocSources);
        }

        return sources;
    }

    /// <summary>
    /// A game's folders, named where the adapter can be asked what to call them.
    /// </summary>
    /// <remarks>
    /// <b>The list has to be complete and the names do not.</b> A target missing from it is a folder
    /// the merged view never looks in, which reports what that folder holds as missing from this
    /// machine - so settings this repo's adapter version cannot read fall back to the persisted
    /// targets, which are paths and keys with no display name. That is the same list with worse
    /// labels rather than a shorter one.
    /// </remarks>
    private IReadOnlyList<(TargetKey Key, string? DisplayName, string Path)> ReadTargets(Game game)
    {
        try
        {
            if (game.GetAdapter(_repo.Adapter).GetLocalCapabilityAdapterFactory<ILocalModAdapter>() is
                Func<ILocalModAdapter> factory)
            {
                return [.. factory().ModTargets.Select(x => (x.Key, x.DisplayName, x.Path))];
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Could not read the targets of game {Game} from its adapter; falling back to the persisted folders.",
                game.Identity);
        }

        return [.. game.Targets.Select(x => (x.Key, (string?)null, x.ModFolder))];
    }

    /// <summary>
    /// Whether a source is scanned. Answered entirely from this catalog, which lives and dies with
    /// the page - nothing about it is persisted.
    /// </summary>
    public bool IsEnabled(ModSource source)
    {
        lock (_lock)
        {
            return _enabledSources.Contains(source.Id);
        }
    }

    /// <summary>
    /// Whether this source has been read at least once this session, whether or not its chip is on
    /// now. <inheritdoc cref="_standbySources" path="/remarks"/>
    /// </summary>
    public bool IsStandby(ModSource source)
    {
        lock (_lock)
        {
            return _standbySources.Contains(source.Id);
        }
    }

    /// <summary>
    /// Switches a source in or out of the merged view. Disabling a game says nothing about
    /// syncing to it - a source is somewhere to find mods, a sync target is a folder sync will make
    /// match a profile, and a game's mod folder simply happens to be both.
    /// </summary>
    public void SetEnabled(ModSource source, bool enabled)
    {
        SetEnabled(source.Id, enabled);
    }

    /// <summary>
    /// The same, by id, for a source that has not been listed yet - the one caller being a page
    /// opened <i>at</i> a particular folder rather than merely opened.
    /// </summary>
    public void SetEnabled(ModSourceId sourceId, bool enabled)
    {
        lock (_lock)
        {
            if (enabled)
            {
                _enabledSources.Add(sourceId);
                _standbySources.Add(sourceId);
            }
            else
            {
                // Left on standby - still read, still refreshed by a rescan, simply not merged in.
                _enabledSources.Remove(sourceId);
            }
        }
    }

    /// <summary>
    /// Adds a folder for this session only. Someone importing from a USB stick should not have that
    /// folder haunting the list for months, so nothing about it is written to disk.
    /// </summary>
    public ModSource AddAdHocSource(string path)
    {
        var id = ModSourceId.ForFolder(path);

        lock (_lock)
        {
            if (_adHocSources.FirstOrDefault(x => x.Id == id) is ModSource existing)
            {
                return existing;
            }

            var source = new ModSource(id, GetFolderDisplayName(path), path, ModSourceKind.AdHoc);
            _adHocSources.Add(source);
            _enabledSources.Add(id);
            _standbySources.Add(id);

            return source;
        }
    }

    public void RemoveAdHocSource(ModSourceId sourceId)
    {
        lock (_lock)
        {
            // Removed rather than switched off, which is the stronger of the two statements: this
            // folder is not somewhere to look at all, so it leaves standby along with everything
            // else and what it contributed goes with it.
            _adHocSources.RemoveAll(x => x.Id == sourceId);
            _enabledSources.Remove(sourceId);
            _standbySources.Remove(sourceId);
            _scans.Remove(sourceId);
        }
    }

    /// <summary>Drops one source's cached scan, so the next read walks that folder again.</summary>
    public void Rescan(ModSourceId sourceId)
    {
        lock (_lock)
        {
            _scans.Remove(sourceId);
        }
    }

    public void RescanAll()
    {
        lock (_lock)
        {
            _scans.Clear();
        }
    }

    /// <summary>
    /// Drops the cached scan of every source that is this folder, so the next read walks it again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>By path, not by source.</b> An apply knows which folder it changed and nothing about which
    /// chips point at it - a game's own mod folder is a source, but so can a folder somebody added by
    /// hand, or Downloads, be the same directory.
    /// </para>
    /// <para>
    /// <b>A source on standby counts.</b> It is switched off, not forgotten: it still contributes to
    /// <see cref="ModCatalogSnapshot.Known"/>, and a scan that outlived the apply would go on offering
    /// versions whose file the apply removed. A source that was never read has nothing cached and is
    /// left alone - dropping nothing is not a reason for anybody to recompose.
    /// </para>
    /// </remarks>
    /// <returns>Whether anything was dropped, which is whether a page showing this catalog is now stale.</returns>
    public bool RescanFolder(string folder)
    {
        var sources = GetSources()
            .Where(x => FileSystemHelper.ArePathsEqual(x.Path, folder))
            .Select(x => x.Id)
            .ToList();

        var dropped = false;

        lock (_lock)
        {
            foreach (var id in sources)
            {
                dropped |= _scans.Remove(id);
            }
        }

        return dropped;
    }

    /// <summary>
    /// Fetches whatever the repo has registered since the last read. Correct after an import, which
    /// only ever adds - a version deleted on the server is invisible to a delta and needs
    /// <see cref="ReloadRegisteredMods"/>.
    /// </summary>
    public void RefreshRegisteredMods()
    {
        lock (_lock)
        {
            _registeredLoad = null;
        }
    }

    public void ReloadRegisteredMods()
    {
        lock (_lock)
        {
            _registeredLoad = null;
            _registeredThrough = null;
            _registered.Clear();
            _usageLoad = null;
        }
    }

    /// <summary>
    /// Drops which profiles depend on what. There is no delta form to lean on - a dependency carries
    /// no timestamp of its own - so this is a full refetch, which the endpoint is shaped for.
    /// </summary>
    public void RefreshUsage()
    {
        lock (_lock)
        {
            _usageLoad = null;
        }
    }

    /// <summary>Everything an import invalidates: the files it consumed, and what the repo now holds.</summary>
    public void Invalidate()
    {
        RescanAll();
        RefreshRegisteredMods();

        // Registering a version does not make a profile depend on it, but the import surface is also
        // where deletes happen, and re-reading a small listing costs less than reasoning about when
        // it is safe not to.
        RefreshUsage();
    }

    /// <summary>
    /// The merged view over the enabled sources, and beside it everything this catalog has read.
    /// Cheap once the scans are warm, which is what makes toggling a source instant.
    /// </summary>
    /// <remarks>
    /// <b>The standby sources are read but not merged.</b> They cost nothing extra here - their scans
    /// are already cached - but they are re-read after a rescan, which is the whole point: a source
    /// this session has looked in keeps contributing to <see cref="ModCatalogSnapshot.Known"/> while
    /// its chip is off, and stops the moment the folder itself stops holding the file. See
    /// <see cref="_standbySources"/>.
    /// </remarks>
    public async Task<ModCatalogSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        var sources = GetSources();
        var enabled = sources.Where(IsEnabled).ToList();
        var standby = sources.Where(x => IsEnabled(x) is false && IsStandby(x)).ToList();

        var scans = enabled.Select(GetOrStartScan).ToList();
        var standbyScans = standby.Select(GetOrStartScan).ToList();
        var registered = GetOrStartRegisteredLoad();
        var usage = GetOrStartUsageLoad();

        var pending = new List<Task>(scans);
        pending.AddRange(standbyScans);
        pending.Add(registered);
        pending.Add(usage);

        // A failing source is reported rather than thrown, so only a failure to reach the server can
        // fault this.
        await Task.WhenAll(pending).WaitAsync(cancellationToken);

        var results = scans.Select(x => x.Result).ToList();
        var byId = results.ToDictionary(x => x.Source.Id);

        var statuses = sources
            .Select(x => byId.TryGetValue(x.Id, out var scan)
                ? new ModSourceStatus(x, true, scan.Mods.Count, scan.Error)
                : new ModSourceStatus(x, false, 0, null))
            .ToList();

        var versions = Merge(results, registered.Result, usage.Result);

        // Merged a second time rather than filtered out of one pass: a version in both an enabled
        // and a standby source has to carry both occurrences in Known and only the enabled one in
        // Versions, which is a different record either way. Skipped entirely where there is no
        // standby source, which is every page that has never switched one off.
        var known = standbyScans.Count == 0
            ? versions
            : Merge([.. results, .. standbyScans.Select(x => x.Result)], registered.Result, usage.Result);

        return new ModCatalogSnapshot(versions, known, statuses);
    }

    /// <summary>
    /// Cancels whatever is still scanning. A caller navigating away has no use for the rest of a
    /// mod folder walk, which is the most expensive thing this app does.
    /// </summary>
    public void Dispose()
    {
        // Deliberately not disposed: a scan may still be inside the token's registration, and
        // disposing a source out from under that is not safe. Nothing here holds a wait handle.
        _cancellation.Cancel();
    }


    private Task<SourceScan> GetOrStartScan(ModSource source)
    {
        lock (_lock)
        {
            if (_scans.TryGetValue(source.Id, out var existing))
            {
                return existing;
            }

            var scan = ScanAsync(source);
            _scans[source.Id] = scan;

            return scan;
        }
    }

    private async Task<SourceScan> ScanAsync(ModSource source)
    {
        await Task.Delay(_scanDelay, _cancellation.Token);

        try
        {
            var mods = await _modAdapter.GetModsFromFolder(source.Path, _cancellation.Token);

            return new SourceScan(source, [.. mods], null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // An unplugged drive or a folder the user deleted marks this one source bad. The rest of
            // the catalog is still worth showing - and the user is told, but only the message, so
            // this is the only place the rest of it survives.
            _logger.LogWarning(ex, "Could not scan mod source {Source} at {Path}.", source.Name, source.Path);

            return new SourceScan(source, [], ex.Message);
        }
    }

    private Task<IReadOnlyList<ModDto>> GetOrStartRegisteredLoad()
    {
        lock (_lock)
        {
            return _registeredLoad ??= LoadRegisteredModsAsync();
        }
    }

    private async Task<IReadOnlyList<ModDto>> LoadRegisteredModsAsync()
    {
        DateTime? since;

        lock (_lock)
        {
            since = _registeredThrough;
        }

        var fetched = new List<ModDto>();
        var latest = since;
        string? cursor = null;

        do
        {
            var page = await _modsClient.GetModsV1Async(_repo.Id, since, cursor, _pageSize, _cancellation.Token);

            foreach (var dto in page.Mods)
            {
                fetched.Add(dto);

                if (latest is null || dto.Updated > latest)
                {
                    latest = dto.Updated;
                }
            }

            cursor = page.NextCursor;
        }
        while (string.IsNullOrEmpty(cursor) is false);

        // Folded in at the end rather than as the pages arrive, so a reload requested mid-fetch
        // clears a set this task is not still writing to.
        lock (_lock)
        {
            foreach (var dto in fetched)
            {
                _registered[GetIdentity(dto)] = dto;
            }

            _registeredThrough = latest;

            return [.. _registered.Values];
        }
    }

    private Task<IReadOnlyDictionary<ModVersionIdentity, ModUsage>> GetOrStartUsageLoad()
    {
        lock (_lock)
        {
            return _usageLoad ??= LoadUsageAsync();
        }
    }

    /// <summary>
    /// Which registered versions the repo's profiles pin, and how many pin each - in their newest
    /// revision and in an older one.
    /// </summary>
    /// <remarks>
    /// Read to exhaustion before it is used, deliberately: absence from the listing is what makes a
    /// version unused, and a half-read listing would call a version unused that a teammate's profile
    /// picked up on the next page. Deleting on that view is exactly the hazard the endpoint exists
    /// to remove. See docs/09-mod-catalog.md#manage.
    /// </remarks>
    private async Task<IReadOnlyDictionary<ModVersionIdentity, ModUsage>> LoadUsageAsync()
    {
        var usage = new Dictionary<ModVersionIdentity, ModUsage>();
        string? cursor = null;

        do
        {
            var page = await _modsClient.GetModUsageV1Async(_repo.Id, cursor, null, _cancellation.Token);

            foreach (var entry in page.Usage)
            {
                // Normalized on the way in for the same reason the mod list is: the server holds
                // whatever casing was registered, and an un-normalized id silently misses its row.
                usage[new ModVersionIdentity(ModKey.From(entry.ModId), ModVersionKey.From(entry.VersionId))] =
                    new ModUsage(entry.CurrentProfileCount, entry.PastProfileCount);
            }

            cursor = page.NextCursor;
        }
        while (string.IsNullOrEmpty(cursor) is false);

        return usage;
    }

    private static IReadOnlyList<CatalogModVersion> Merge(
        IReadOnlyList<SourceScan> scans,
        IReadOnlyList<ModDto> registered,
        IReadOnlyDictionary<ModVersionIdentity, ModUsage> usage)
    {
        // Deduplication is on (ModId, VersionId); every source a version turned up in is kept, so a
        // row can say where it came from and two sources disagreeing about the bytes stays visible.
        var occurrences = new Dictionary<ModVersionIdentity, List<ModOccurrence>>();
        var local = new Dictionary<ModVersionIdentity, LocalMod>();

        foreach (var scan in scans)
        {
            foreach (var mod in scan.Mods)
            {
                var identity = new ModVersionIdentity(mod.Id, mod.Version);

                local.TryAdd(identity, mod);

                if (occurrences.TryGetValue(identity, out var found) is false)
                {
                    occurrences[identity] = found = [];
                }

                found.Add(new ModOccurrence(scan.Source, mod.FilePath, mod.FileLength, mod.GetStream));
            }
        }

        var versions = new List<CatalogModVersion>(registered.Count + local.Count);

        foreach (var dto in registered)
        {
            var identity = GetIdentity(dto);

            versions.Add(Create(
                identity,
                dto,
                local.GetValueOrDefault(identity),
                occurrences.GetValueOrDefault(identity),
                // Absent from the usage listing means no profile pins it, which is only true because
                // the listing was read whole.
                usage.GetValueOrDefault(identity)));
        }

        var registeredIdentities = registered.Select(GetIdentity).ToHashSet();

        foreach (var (identity, mod) in local)
        {
            if (registeredIdentities.Contains(identity) is false)
            {
                // No usage: a version the repo does not hold has no dependency that could name it.
                versions.Add(Create(identity, null, mod, occurrences.GetValueOrDefault(identity), null));
            }
        }

        return versions;
    }

    private static CatalogModVersion Create(
        ModVersionIdentity identity,
        ModDto? dto,
        LocalMod? local,
        List<ModOccurrence>? occurrences,
        ModUsage? usage)
    {
        // The registered record is the shared truth, so it wins where both exist - two members
        // looking at the same registered version should read the same thing. That extends to
        // imagery: the archive's images are carried for a version nobody has registered and for
        // deriving what a registered one is missing, but what a registered version renders is
        // whatever the repo points at.
        return new CatalogModVersion(
            identity.ModId,
            identity.VersionId,
            dto?.DisplayName ?? local?.Name ?? identity.ModId.Value,
            dto?.Description ?? local?.Description ?? string.Empty,
            IsLocal: occurrences is { Count: > 0 },
            IsOnServer: dto is not null,
            // An unregistered version answers from its own archive, which is what lets a row show
            // the lock before anything is imported and what registration then sends. A registered
            // one answers from the repo, so two members reading the same version read the same
            // lock state even where only one of them holds the file.
            Locked: dto?.Locked ?? local?.Locked ?? false)
        {
            Author = local?.Author,
            Icon = local?.Icon,
            Images = local?.Images ?? [],
            ServerImages = dto is null ? [] : [.. dto.Images.Select(ModImageReference.FromDto)],
            FoundIn = occurrences ?? [],
            ContentHash = dto?.ContentHash,
            SizeBytes = dto?.SizeBytes,
            SequenceNumber = dto?.SequenceNumber,
            DeletionScheduledFor = dto?.DeletionScheduledFor,
            DeletionReason = dto?.DeletionReason,
            Usage = usage
        };
    }

    private static ModVersionIdentity GetIdentity(ModDto dto)
    {
        // The server holds whatever casing was registered, so an id crossing this boundary is
        // normalized too - otherwise the join against a scan silently misses.
        return new(ModKey.From(dto.ModId), ModVersionKey.From(dto.VersionId));
    }

    private static string GetFolderDisplayName(string path)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(path);

        return Path.GetFileName(trimmed) is { Length: > 0 } name ? name : trimmed;
    }


    public class Factory(
        IModsClient modsClient,
        ILogger<ModCatalog> logger)
    {
        public ModCatalog Create(Repo repo) => new(repo, modsClient, logger);
    }
}

/// <summary>The merged set, plus what every source contributed to it.</summary>
/// <param name="Versions">
/// What the <em>enabled</em> sources hold, merged with everything the repo has registered. What a
/// list of mods is a list <em>of</em>.
/// </param>
/// <param name="Known">
/// The same, widened to the sources on standby - every source this session has switched on at least
/// once, whether or not it still is.
/// </param>
/// <remarks>
/// <b>Two sets, because a chip and a rescan are different events.</b> Unticking a source must not
/// take a version out of a draft's version selector or off the update planner - it is a statement
/// about what is being looked at - while a rescan that no longer finds a file must. A single set
/// cannot do both, and an append-only accumulation kept by the caller does the first at the cost of
/// never being able to do the second. <see cref="Known"/> is a superset of <see cref="Versions"/>
/// and both are recomputed from the current scans, so neither can outlive what is actually on disk.
/// </remarks>
public record ModCatalogSnapshot(
    IReadOnlyList<CatalogModVersion> Versions,
    IReadOnlyList<CatalogModVersion> Known,
    IReadOnlyList<ModSourceStatus> Sources);

/// <param name="Error">
/// Why this source contributed nothing, when it should have. Set rather than thrown, so one
/// unreadable folder marks one source bad instead of failing the whole catalog.
/// </param>
public record ModSourceStatus(ModSource Source, bool IsEnabled, int ModCount, string? Error)
{
    public bool HasFailed => Error is not null;
}

internal record SourceScan(ModSource Source, IReadOnlyList<LocalMod> Mods, string? Error);
