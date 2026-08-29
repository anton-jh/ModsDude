using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
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
        }
    }

    /// <summary>Everything an import invalidates: the files it consumed, and what the repo now holds.</summary>
    public void Invalidate()
    {
        RescanAll();
        RefreshRegisteredMods();
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

        var pending = new List<Task>(scans) { registered };

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

        return new ModCatalogSnapshot(Merge(results, registered.Result), statuses);
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

    private static IReadOnlyList<CatalogModVersion> Merge(
        IReadOnlyList<SourceScan> scans,
        IReadOnlyList<ModDto> registered)
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

            versions.Add(Create(identity, dto, local.GetValueOrDefault(identity), occurrences.GetValueOrDefault(identity)));
        }

        var registeredIdentities = registered.Select(GetIdentity).ToHashSet();

        foreach (var (identity, mod) in local)
        {
            if (registeredIdentities.Contains(identity) is false)
            {
                versions.Add(Create(identity, null, mod, occurrences.GetValueOrDefault(identity)));
            }
        }

        return versions;
    }

    private static CatalogModVersion Create(
        ModVersionIdentity identity,
        ModDto? dto,
        LocalMod? local,
        List<ModOccurrence>? occurrences)
    {
        // The registered record is the shared truth, so it wins where both exist - two members
        // looking at the same registered version should read the same thing. Imagery still comes
        // from the archive: serving it from the repo is a separate piece of work.
        return new CatalogModVersion(
            identity.ModId,
            identity.VersionId,
            dto?.DisplayName ?? local?.Name ?? identity.ModId.Value,
            dto?.Description ?? local?.Description ?? string.Empty,
            IsLocal: occurrences is { Count: > 0 },
            IsOnServer: dto is not null,
            Locked: dto?.Locked ?? false)
        {
            Author = local?.Author,
            Icon = local?.Icon,
            Images = local?.Images ?? [],
            FoundIn = occurrences ?? [],
            ContentHash = dto?.ContentHash,
            SequenceNumber = dto?.SequenceNumber
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
