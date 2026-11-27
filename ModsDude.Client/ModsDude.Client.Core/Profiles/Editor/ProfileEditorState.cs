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
/// selector was set to, or else the one a savegame source prefers, or else the newest one not
/// dismissed, or else the newest.
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

    public required int UnsettledCount { get; init; }

    public int ResultCount => Pinned.Count(x => x.IsTakenOut is false);
    public int TakenOutCount => Pinned.Count(x => x.IsTakenOut);
    public int PendingCount => Pinned.Count(x => x.IsPending);

    /// <summary>Every pin the Updates filter would show, whatever the search.</summary>
    public int UpdatesFilterCount => Pinned.Count(x => x.ShowsUnderUpdates);

    /// <summary>How many pins a save will download first.</summary>
    public int DownloadCount => Pinned.Count(x => x.IsPending && x.Version.Download is not null);


    private static readonly IComparer<ModKey> _byModId = Comparer<ModKey>.Create((left, right) => string.CompareOrdinal(left.Value, right.Value));

    public static ProfileEditorState Compute(ProfileEditorInputs inputs)
    {
        var savegameMatches = inputs.SavegameSources.Select(x => x.Match(inputs.Catalog)).ToList();
        var offered = OfferedVersions(inputs, savegameMatches);

        var available = AvailableRows(inputs, offered, Preferred(savegameMatches));
        var (pinned, updates) = PinnedRows(inputs, offered);

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
            UnsettledCount = pinned.Count(x => x.HasUnsettledVersion)
        };
    }


    /// <summary>
    /// What the enabled sources offer, by mod: what they hold, what the enabled remote sources offer,
    /// what the enabled profiles pin, and the versions found of what the enabled savegames list.
    /// </summary>
    private static SortedDictionary<ModKey, HashSet<ModVersionKey>> OfferedVersions(
        ProfileEditorInputs inputs,
        IReadOnlyList<SavegameModMatches> savegameMatches)
    {
        var offered = new SortedDictionary<ModKey, HashSet<ModVersionKey>>(_byModId);

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
            if (version.IsLocal || version.Download is not null || (inputs.IncludeRegistered && version.IsOnServer))
            {
                Offer(version.ModId, version.VersionId);
            }
        }

        foreach (var pin in inputs.ProfileSources.SelectMany(x => x.Pins))
        {
            Offer(pin.ModId, pin.VersionId);
        }

        foreach (var match in savegameMatches.SelectMany(x => x.Found))
        {
            foreach (var version in match.Versions)
            {
                Offer(match.ModId, version);
            }
        }

        return offered;
    }

    /// <summary>The version each savegame source prefers, by mod. The first source to prefer one wins.</summary>
    private static Dictionary<ModKey, ModVersionKey> Preferred(IReadOnlyList<SavegameModMatches> savegameMatches)
    {
        var preferred = new Dictionary<ModKey, ModVersionKey>();

        foreach (var match in savegameMatches.SelectMany(x => x.Found))
        {
            if (match.Preferred is ModVersionKey version)
            {
                preferred.TryAdd(match.ModId, version);
            }
        }

        return preferred;
    }

    private static List<AvailableModRow> AvailableRows(
        ProfileEditorInputs inputs,
        SortedDictionary<ModKey, HashSet<ModVersionKey>> offered,
        IReadOnlyDictionary<ModKey, ModVersionKey> preferred)
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

            var selected = Pick(inputs.AvailableChoices, modId, ordered)
                ?? Pick(preferred, modId, ordered)
                ?? ordered.LastOrDefault(x => draft.Dismissed.Contains(x.Identity) is false)
                ?? ordered[^1];

            var status = selected.IsOnServer
                ? AvailableModStatus.InRepo
                : set?.NewestRegistered is CatalogModVersion newest && set.IsAfter(selected.VersionId, newest.VersionId)
                    ? AvailableModStatus.NewVersion
                    : AvailableModStatus.New;

            var key = new ModListSortKey(modId, selected.Name, selected.Registered, selected.Attributes);

            rows.Add(new AvailableModRow(
                modId,
                selected,
                Options(set, ordered, draft),
                status,
                set?.CouldNotCompareToNewest(selected.VersionId) ?? false,
                draft.Ignored.Contains(modId),
                inputs.ProfileSources.Any(x => x.Locks(selected.Identity)),
                DescribeSources(selected, nameSources, ListedBy(inputs, modId)),
                inputs.AvailableSort.Caption(key, inputs.Now, ModListDateWording.ImportedToRepo),
                inputs.AvailableSort.Describe(key, ModListDateWording.ImportedToRepo)));
        }

        return rows;
    }

    private static (List<PinnedModRow> Rows, ProfileModUpdatePlan Updates) PinnedRows(
        ProfileEditorInputs inputs,
        SortedDictionary<ModKey, HashSet<ModVersionKey>> offered)
    {
        var catalog = inputs.Catalog;
        var draft = inputs.Draft;

        var effective = draft.Pins.Values
            .Order(Comparer<ProfileModPin>.Create((left, right) => _byModId.Compare(left.ModId, right.ModId)))
            .Select(x => x with { Lock = new ProfileModLock(Resolve(catalog, x).Locked, x.Lock.ByProfile) })
            .ToList();

        var updates = ProfileModUpdates.Plan(
            effective,
            catalog.Index,
            x => catalog.IsVisible(x) && draft.Dismissed.Contains(x.Identity) is false);
        var updatesByMod = updates.Available.Concat(updates.Skipped).ToDictionary(x => x.ModId);
        var effectiveByMod = effective.ToDictionary(x => x.ModId);

        var rows = new List<PinnedModRow>();

        foreach (var modId in draft.AllMods.Order(_byModId))
        {
            var pin = effectiveByMod.GetValueOrDefault(modId);
            var saved = draft.Saved.GetValueOrDefault(modId);
            var current = pin ?? saved!;
            var version = Resolve(catalog, current);
            var set = catalog.Index.GetValueOrDefault(modId);

            var options = pin is null
                ? Options(set, [version], draft)
                : Options(set, Ordered(catalog, modId, catalog.IsVisible, [version.VersionId]), draft);

            var added = inputs.SavedDates.TryGetValue(modId, out var date) && date.Version == current.VersionId
                ? date.Added
                : (DateTimeOffset?)null;

            var rowLock = new ProfileModLock(version.Locked, current.Lock.ByProfile);

            var unsettled = pin is not null
                && rowLock.IsLocked is false
                && set is not null
                && set.Order.Any(x => x.VersionId != pin.VersionId
                    && x.IsOnServer is false
                    && catalog.IsVisible(x)
                    && set.CouldNotCompareToNewest(x.VersionId));

            var key = new ModListSortKey(modId, version.Name, added, version.Attributes);

            rows.Add(new PinnedModRow(
                modId,
                version,
                options,
                pin,
                saved,
                rowLock,
                ProfileModTouches.Classify(saved, pin),
                ProfileModTouches.Describe(saved, pin),
                pin is null ? null : updatesByMod.GetValueOrDefault(modId),
                unsettled,
                offered.ContainsKey(modId) is false && inputs.SavegameSources.Any(x => x.Lists(modId)) is false,
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

    private static CatalogModVersion? Pick(
        IReadOnlyDictionary<ModKey, ModVersionKey> choices,
        ModKey modId,
        List<CatalogModVersion> ordered)
        => choices.TryGetValue(modId, out var choice) ? ordered.FirstOrDefault(x => x.VersionId == choice) : null;

    /// <summary>The enabled profile and savegame sources that list the mod, by name, in the order they were added.</summary>
    private static List<string> ListedBy(ProfileEditorInputs inputs, ModKey modId)
        => [.. inputs.ProfileSources.Where(x => x.Holds(modId)).Select(x => x.Source.Name),
            .. inputs.SavegameSources.Where(x => x.Lists(modId)).Select(x => x.Source.Name)];

    /// <summary>
    /// Where a mod listed by a profile or savegame source has its file, after the sources listing it.
    /// Otherwise the folders a version was found in, where more than one source is enabled, or the
    /// remote source that offers it.
    /// </summary>
    private static string? DescribeSources(CatalogModVersion version, bool nameFolders, IReadOnlyList<string> listedBy)
    {
        if (listedBy.Count > 0)
        {
            return $"{string.Join(", ", listedBy)} → {Location(version)}";
        }

        if (version.Download is RemoteOffer offer)
        {
            return offer.SourceName;
        }

        return nameFolders && version.FoundIn.Count > 0 ? Folders(version) : null;
    }

    /// <summary>Where a version's file comes from: the remote source offering it, the folders holding it, or the repo.</summary>
    internal static string Location(CatalogModVersion version) => version switch
    {
        { Download: RemoteOffer offer } => offer.SourceName,
        { FoundIn.Count: > 0 } => Folders(version),
        _ => ModSource.ThisRepo.Name
    };

    private static string Folders(CatalogModVersion version)
        => string.Join(", ", version.FoundIn.Select(x => x.Source.Name).Distinct());

    private static List<ProfileModVersionOption> Options(ModVersionSet? set, List<CatalogModVersion> ordered, ProfileDraft draft)
        => [.. Enumerable.Reverse(ordered).Select(x => new ProfileModVersionOption(
            x,
            set?.CouldNotCompareToNewest(x.VersionId) ?? false,
            draft.Dismissed.Contains(x.Identity)))];

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
            PinnedModFilter.Updates => row.ShowsUnderUpdates,
            PinnedModFilter.Locked => row.IsTakenOut is false && row.Lock.IsLocked,
            PinnedModFilter.NotInSources => row.IsTakenOut is false && row.NotInSources,
            _ => true
        };

        return passes && Matches(inputs, row.Version);
    }

    private static ModListSortKey SortKey(AvailableModRow row)
        => new(row.ModId, row.Version.Name, row.Version.Registered, row.Version.Attributes);

    private static ModListSortKey SortKey(PinnedModRow row)
        => new(row.ModId, row.Version.Name, row.Added, row.Version.Attributes);
}
