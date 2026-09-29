using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModVersions;

namespace ModsDude.Client.Core.Profiles.Editor;

/// <summary>
/// Everything the profile mod editor shows, computed from its inputs and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>The left list</b> is every mod an enabled source offers that the profile neither pins nor
/// held when it was read. Each row offers the versions the sources offer, and points at the one its
/// selector was set to or the newest.
/// </para>
/// <para>
/// <b>The right list</b> is the draft: every pin, plus every saved pin the draft has taken out.
/// Its selectors offer visible versions, and so do its updates - see <see cref="ProfileEditorCatalog"/>.
/// </para>
/// </remarks>
public sealed record ProfileEditorState
{
    /// <summary>Every left row, shown or not.</summary>
    public required IReadOnlyList<AvailableModRow> Available { get; init; }

    /// <summary>The left rows the search, the filter and the ignore toggle let through, in order.</summary>
    public required IReadOnlyList<AvailableModRow> AvailableShown { get; init; }

    /// <summary>Every left row but those the ignore toggle is hiding.</summary>
    public required int AvailableTotal { get; init; }

    /// <summary>How many rows the ignore toggle is hiding or dimming among what the search and filter let through.</summary>
    public required int IgnoredCount { get; init; }

    public required IReadOnlyList<PinnedModRow> Pinned { get; init; }
    public required IReadOnlyList<PinnedModRow> PinnedShown { get; init; }

    public required ProfileModUpdatePlan Updates { get; init; }

    /// <summary>How many pinned mods have a newer version on a remote source, locked ones apart.</summary>
    public required int RemoteUpdateCount { get; init; }
    public required int RemoteLockedUpdateCount { get; init; }

    public required int UnsettledCount { get; init; }

    public required IReadOnlyDictionary<ModSourceId, int> RemoteOfferCounts { get; init; }

    public int ResultCount => Pinned.Count(x => x.IsTakenOut is false);
    public int TakenOutCount => Pinned.Count(x => x.IsTakenOut);
    public int PendingCount => Pinned.Count(x => x.IsPending);


    public static ProfileEditorState Compute(ProfileEditorInputs inputs)
    {
        var offered = OfferedVersions(inputs);
        var remoteOffers = RemoteOffers(inputs, out var remoteCounts);

        var available = AvailableRows(inputs, offered, remoteOffers);
        var (pinned, updates) = PinnedRows(inputs, offered, remoteOffers);

        var availableShown = available
            .Where(x => IsShown(inputs, x))
            .Order(Comparer<AvailableModRow>.Create((left, right) => inputs.AvailableSort.Compare(SortKey(left), SortKey(right))))
            .ToList();

        var pinnedShown = pinned
            .Where(x => IsShown(inputs, x))
            .Order(Comparer<PinnedModRow>.Create((left, right) => inputs.PinnedSort.Compare(SortKey(left), SortKey(right))))
            .ToList();

        return new ProfileEditorState
        {
            Available = available,
            AvailableShown = availableShown,
            AvailableTotal = available.Count(x => inputs.ShowIgnored || x.IsIgnored is false),
            IgnoredCount = available.Count(x => x.IsIgnored && Matches(inputs, x.Version) && PassesFilter(inputs, x)),
            Pinned = pinned,
            PinnedShown = pinnedShown,
            Updates = updates,
            RemoteUpdateCount = pinned.Count(x => x.Pin is not null && x.RemoteOffer is not null && x.Lock.IsLocked is false),
            RemoteLockedUpdateCount = pinned.Count(x => x.Pin is not null && x.RemoteOffer is not null && x.Lock.IsLocked),
            UnsettledCount = pinned.Count(x => x.HasUnsettledVersion),
            RemoteOfferCounts = remoteCounts
        };
    }


    /// <summary>What the enabled sources offer, by mod: what they hold, and what the enabled profiles pin.</summary>
    private static Dictionary<ModKey, HashSet<ModVersionKey>> OfferedVersions(ProfileEditorInputs inputs)
    {
        var offered = new Dictionary<ModKey, HashSet<ModVersionKey>>();

        void Offer(ModKey modId, ModVersionKey versionId)
        {
            if (offered.TryGetValue(modId, out var versions) is false)
            {
                offered[modId] = versions = [];
            }

            versions.Add(versionId);
        }

        foreach (var version in inputs.Catalog.Visible)
        {
            if (version.IsLocal || (inputs.IncludeRegistered && version.IsOnServer))
            {
                Offer(version.ModId, version.VersionId);
            }
        }

        foreach (var pin in inputs.ProfileSources.SelectMany(x => x.Pins))
        {
            Offer(pin.ModId, pin.VersionId);
        }

        return offered;
    }

    private static Dictionary<ModKey, RemoteOfferInfo> RemoteOffers(
        ProfileEditorInputs inputs,
        out IReadOnlyDictionary<ModSourceId, int> counts)
    {
        var offers = new Dictionary<ModKey, RemoteOfferInfo>();
        var perSource = new Dictionary<ModSourceId, int>();

        foreach (var remote in inputs.RemoteSources)
        {
            var newer = RemoteModOffers.Newer(remote.Answers, inputs.Catalog.Index, inputs.Catalog.Comparer);

            perSource[remote.Id] = newer.Count;

            foreach (var (modId, offer) in newer)
            {
                offers.TryAdd(modId, new RemoteOfferInfo(offer, remote.DisplayName));
            }
        }

        counts = perSource;

        return offers;
    }

    private static List<AvailableModRow> AvailableRows(
        ProfileEditorInputs inputs,
        Dictionary<ModKey, HashSet<ModVersionKey>> offered,
        Dictionary<ModKey, RemoteOfferInfo> remoteOffers)
    {
        var catalog = inputs.Catalog;
        var draft = inputs.Draft;
        var nameSources = catalog.Snapshot.Sources.Count(x => x.IsEnabled) > 1;
        var rows = new List<AvailableModRow>();

        foreach (var (modId, versionIds) in offered)
        {
            if (draft.Pins.ContainsKey(modId) || draft.Saved.ContainsKey(modId))
            {
                continue;
            }

            var set = catalog.Index.GetValueOrDefault(modId);
            var ordered = Ordered(catalog, modId, x => versionIds.Contains(x.VersionId), versionIds);

            var selected = inputs.AvailableChoices.TryGetValue(modId, out var choice)
                    && ordered.FirstOrDefault(x => x.VersionId == choice) is CatalogModVersion chosen
                ? chosen
                : ordered[^1];

            var status = selected.IsOnServer
                ? AvailableModStatus.InRepo
                : set?.NewestRegistered is CatalogModVersion newest && set.IsAfter(selected.VersionId, newest.VersionId)
                    ? AvailableModStatus.NewVersion
                    : AvailableModStatus.New;

            var key = new ModListSortKey(selected.Name, selected.Registered, selected.Attributes);

            rows.Add(new AvailableModRow(
                modId,
                selected,
                Options(set, ordered),
                status,
                set?.CouldNotCompareToNewest(selected.VersionId) ?? false,
                draft.Ignored.Contains(modId),
                inputs.ProfileSources.Any(x => x.Locks(selected.Identity)),
                nameSources && selected.FoundIn.Count > 0
                    ? string.Join(", ", selected.FoundIn.Select(x => x.Source.Name).Distinct())
                    : null,
                remoteOffers.GetValueOrDefault(modId),
                inputs.AvailableSort.Caption(key, inputs.Now, ModListDateWording.ImportedToRepo),
                inputs.AvailableSort.Describe(key, ModListDateWording.ImportedToRepo)));
        }

        return rows;
    }

    private static (List<PinnedModRow> Rows, ProfileModUpdatePlan Updates) PinnedRows(
        ProfileEditorInputs inputs,
        Dictionary<ModKey, HashSet<ModVersionKey>> offered,
        Dictionary<ModKey, RemoteOfferInfo> remoteOffers)
    {
        var catalog = inputs.Catalog;
        var draft = inputs.Draft;

        var effective = draft.Pins.Values
            .Select(x => x with { Lock = new ProfileModLock(Resolve(catalog, x).Locked, x.Lock.ByProfile) })
            .ToList();

        var updates = ProfileModUpdates.Plan(effective, catalog.Index, catalog.IsVisible);
        var updatesByMod = updates.Available.Concat(updates.Skipped).ToDictionary(x => x.ModId);
        var effectiveByMod = effective.ToDictionary(x => x.ModId);

        var rows = new List<PinnedModRow>();

        foreach (var modId in draft.AllMods)
        {
            var pin = effectiveByMod.GetValueOrDefault(modId);
            var saved = draft.Saved.GetValueOrDefault(modId);
            var current = pin ?? saved!;
            var version = Resolve(catalog, current);
            var set = catalog.Index.GetValueOrDefault(modId);

            var options = pin is null
                ? [new ProfileModVersionOption(version, set?.CouldNotCompareToNewest(version.VersionId) ?? false)]
                : Options(set, Ordered(catalog, modId, catalog.IsVisible, [version.VersionId]));

            var added = inputs.SavedDates.TryGetValue(modId, out var date) && date.Version == current.VersionId
                ? date.Added
                : (DateTimeOffset?)null;

            var unsettled = pin is not null
                && set is not null
                && set.Order.Any(x => x.VersionId != pin.VersionId
                    && x.IsOnServer is false
                    && catalog.IsVisible(x)
                    && set.CouldNotCompareToNewest(x.VersionId));

            var key = new ModListSortKey(version.Name, added, version.Attributes);

            rows.Add(new PinnedModRow(
                modId,
                version,
                options,
                pin,
                saved,
                new ProfileModLock(version.Locked, current.Lock.ByProfile),
                ProfileModTouches.Classify(saved, pin),
                ProfileModTouches.Describe(saved, pin),
                pin is null ? null : updatesByMod.GetValueOrDefault(modId),
                pin is null ? null : remoteOffers.GetValueOrDefault(modId),
                unsettled,
                offered.ContainsKey(modId) is false,
                added,
                inputs.PinnedSort.Caption(key, inputs.Now, ModListDateWording.AddedToProfile),
                inputs.PinnedSort.Describe(key, ModListDateWording.AddedToProfile)));
        }

        return (rows, updates);
    }

    /// <summary>
    /// One mod's versions that <paramref name="include"/> lets through, oldest first, with any of
    /// <paramref name="required"/> the ordering has never heard of at the oldest end - where a version
    /// that cannot be placed cannot read as the newest.
    /// </summary>
    private static List<CatalogModVersion> Ordered(
        ProfileEditorCatalog catalog,
        ModKey modId,
        Func<CatalogModVersion, bool> include,
        IEnumerable<ModVersionKey> required)
    {
        var set = catalog.Index.GetValueOrDefault(modId);

        var ordered = set?.Order
            .Where(x => include(x) || required.Contains(x.VersionId))
            .Select(x => catalog.Resolve(x.Identity))
            .ToList() ?? [];

        var unplaced = required
            .Where(x => set?.Find(x) is null)
            .Select(x => catalog.Resolve(new ModVersionIdentity(modId, x)));

        ordered.InsertRange(0, unplaced);

        return ordered;
    }

    private static List<ProfileModVersionOption> Options(ModVersionSet? set, List<CatalogModVersion> ordered)
        => [.. Enumerable.Reverse(ordered).Select(x => new ProfileModVersionOption(x, set?.CouldNotCompareToNewest(x.VersionId) ?? false))];

    private static CatalogModVersion Resolve(ProfileEditorCatalog catalog, ProfileModPin pin)
        => catalog.Resolve(new ModVersionIdentity(pin.ModId, pin.VersionId));

    private static bool Matches(ProfileEditorInputs inputs, CatalogModVersion version)
        => inputs.Search.Matches(version.Attributes, version.Name, version.ModId.Value, version.Author);

    private static bool PassesFilter(ProfileEditorInputs inputs, AvailableModRow row) => inputs.AvailableFilter switch
    {
        AvailableModFilter.New => row.Version.IsOnServer is false,
        AvailableModFilter.Unordered => row.OrderNotSettled,
        _ => true
    };

    private static bool IsShown(ProfileEditorInputs inputs, AvailableModRow row)
        => (inputs.ShowIgnored || row.IsIgnored is false)
        && PassesFilter(inputs, row)
        && Matches(inputs, row.Version);

    private static bool IsShown(ProfileEditorInputs inputs, PinnedModRow row)
    {
        var passes = inputs.PinnedFilter switch
        {
            PinnedModFilter.Result => row.IsTakenOut is false,
            PinnedModFilter.Changes => row.Touch is not ProfileModTouch.None,
            PinnedModFilter.Updates => row.IsTakenOut is false
                && (row.Update is not null || row.RemoteOffer is not null || row.HasUnsettledVersion),
            PinnedModFilter.Locked => row.IsTakenOut is false && row.Lock.IsLocked,
            PinnedModFilter.NotInSources => row.IsTakenOut is false && row.NotInSources,
            _ => true
        };

        return passes && Matches(inputs, row.Version);
    }

    private static ModListSortKey SortKey(AvailableModRow row)
        => new(row.Version.Name, row.Version.Registered, row.Version.Attributes);

    private static ModListSortKey SortKey(PinnedModRow row)
        => new(row.Version.Name, row.Added, row.Version.Attributes);
}
