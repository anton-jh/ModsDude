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
    private readonly ClientSettingsRepository _settings;

    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _lock = new();

    private readonly Dictionary<ModSourceId, Task<SourceScan>> _scans = [];
    private readonly List<ModSource> _adHocSources = [];
    private readonly HashSet<ModSourceId> _disabledAdHocSources = [];

    /// <summary>Registered versions accumulated across delta fetches, keyed by their join key.</summary>
    private readonly Dictionary<ModVersionIdentity, ModDto> _registered = [];

    private Task<IReadOnlyList<ModDto>>? _registeredLoad;
    private DateTime? _registeredThrough;
    private Task<IReadOnlyDictionary<ModVersionIdentity, int>>? _usageLoad;


    public ModCatalog(
        Repo repo,
        IModsClient modsClient,
        ClientSettingsRepository settings)
    {
        _repo = repo;
        _modsClient = modsClient;
        _settings = settings;
        _modAdapter = repo.Adapter.GetBaseCapabilityAdapterFactory<IBaseModAdapter>()?.Invoke()
            ?? throw UserFriendlyException.RepoNoModSupport();
    }


    /// <summary>
    /// Every source currently available, standing ones first. Rebuilt on each call, because the
    /// instance list and the settings behind it are live.
    /// </summary>
    public IReadOnlyList<ModSource> GetSources()
    {
        var sources = new List<ModSource>();

        foreach (var instance in _repo.LocalInstances)
        {
            if (string.IsNullOrWhiteSpace(instance.ModFolder))
            {
                continue;
            }

            sources.Add(new ModSource(
                ModSourceId.ForInstance(instance.Id),
                instance.Name,
                instance.ModFolder,
                ModSourceKind.Instance));
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
    /// Whether a source is scanned. Standing sources answer from machine-wide settings; an ad-hoc
    /// source answers from this catalog, since it stops existing when the page does.
    /// </summary>
    public bool IsEnabled(ModSource source)
    {
        if (source.Kind is ModSourceKind.AdHoc)
        {
            lock (_lock)
            {
                return _disabledAdHocSources.Contains(source.Id) is false;
            }
        }

        return _settings.IsSourceDisabled(source.Id) is false;
    }

    /// <summary>
    /// Switches a source in or out of the merged view. Disabling an instance says nothing about
    /// syncing to it - a source is somewhere to find mods, a sync target is a folder sync will make
    /// match a profile, and an instance's mod folder simply happens to be both.
    /// </summary>
    public void SetEnabled(ModSource source, bool enabled)
    {
        if (source.Kind is ModSourceKind.AdHoc)
        {
            lock (_lock)
            {
                if (enabled)
                {
                    _disabledAdHocSources.Remove(source.Id);
                }
                else
                {
                    _disabledAdHocSources.Add(source.Id);
                }
            }

            return;
        }

        _settings.SetSourceDisabled(source.Id, enabled is false);
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

            return source;
        }
    }

    public void RemoveAdHocSource(ModSourceId sourceId)
    {
        lock (_lock)
        {
            _adHocSources.RemoveAll(x => x.Id == sourceId);
            _disabledAdHocSources.Remove(sourceId);
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
    /// The merged view over the enabled sources. Cheap once the scans are warm, which is what makes
    /// toggling a source instant.
    /// </summary>
    public async Task<ModCatalogSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        var sources = GetSources();
        var enabled = sources.Where(IsEnabled).ToList();

        var scans = enabled.Select(GetOrStartScan).ToList();
        var registered = GetOrStartRegisteredLoad();
        var usage = GetOrStartUsageLoad();

        var pending = new List<Task>(scans) { registered, usage };

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

        return new ModCatalogSnapshot(Merge(results, registered.Result, usage.Result), statuses);
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
            // the catalog is still worth showing.
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

    private Task<IReadOnlyDictionary<ModVersionIdentity, int>> GetOrStartUsageLoad()
    {
        lock (_lock)
        {
            return _usageLoad ??= LoadUsageAsync();
        }
    }

    /// <summary>
    /// Which registered versions the repo's profiles pin, and how many pin each.
    /// </summary>
    /// <remarks>
    /// Read to exhaustion before it is used, deliberately: absence from the listing is what makes a
    /// version unused, and a half-read listing would call a version unused that a teammate's profile
    /// picked up on the next page. Deleting on that view is exactly the hazard the endpoint exists
    /// to remove. See docs/09-mod-catalog.md#manage.
    /// </remarks>
    private async Task<IReadOnlyDictionary<ModVersionIdentity, int>> LoadUsageAsync()
    {
        var usage = new Dictionary<ModVersionIdentity, int>();
        string? cursor = null;

        do
        {
            var page = await _modsClient.GetModUsageV1Async(_repo.Id, cursor, null, _cancellation.Token);

            foreach (var entry in page.Usage)
            {
                // Normalized on the way in for the same reason the mod list is: the server holds
                // whatever casing was registered, and an un-normalized id silently misses its row.
                usage[new ModVersionIdentity(ModKey.From(entry.ModId), ModVersionKey.From(entry.VersionId))] = entry.ProfileCount;
            }

            cursor = page.NextCursor;
        }
        while (string.IsNullOrEmpty(cursor) is false);

        return usage;
    }

    private static IReadOnlyList<CatalogModVersion> Merge(
        IReadOnlyList<SourceScan> scans,
        IReadOnlyList<ModDto> registered,
        IReadOnlyDictionary<ModVersionIdentity, int> usage)
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
        int? usedByProfiles)
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
            SequenceNumber = dto?.SequenceNumber,
            UsedByProfiles = usedByProfiles
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
        ClientSettingsRepository settings)
    {
        public ModCatalog Create(Repo repo) => new(repo, modsClient, settings);
    }
}

/// <summary>The merged set, plus what every source contributed to it.</summary>
public record ModCatalogSnapshot(
    IReadOnlyList<CatalogModVersion> Versions,
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
