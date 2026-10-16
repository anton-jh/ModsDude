using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModVersions;
using ModsDude.Client.Core.RemoteSources;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Core.Profiles.Editor;

/// <summary>
/// One catalog snapshot, indexed once, with the remote sources' offers placed on top.
/// </summary>
/// <remarks>
/// A version is <em>visible</em> when the repo holds it, an enabled source has its file, or an enabled
/// remote source offers it: only those are offered in selectors and as updates. Versions from sources
/// on standby, and offers of disabled remote sources, are still ordered and still resolve the pins that
/// point at them, so a pending pin keeps its file or its download.
/// </remarks>
public sealed class ProfileEditorCatalog
{
    private readonly IReadOnlyDictionary<ModKey, ModVersionSet> _snapshotIndex;
    private readonly Dictionary<ModVersionIdentity, CatalogModVersion> _snapshotVisible;
    private readonly Dictionary<ModVersionIdentity, CatalogModVersion> _snapshotKnown;

    private readonly Dictionary<ModVersionIdentity, CatalogModVersion> _visible;
    private readonly Dictionary<ModVersionIdentity, CatalogModVersion> _known;


    public ProfileEditorCatalog(ModCatalogSnapshot snapshot, IModVersionComparer comparer)
    {
        Snapshot = snapshot;
        Comparer = comparer;

        _snapshotVisible = Deduplicate(snapshot.Versions);
        _snapshotKnown = Deduplicate(snapshot.Known);
        _snapshotIndex = ModVersionIndex.Build(_snapshotKnown.Values, comparer);

        _visible = _snapshotVisible;
        _known = _snapshotKnown;
        Index = _snapshotIndex;
    }

    private ProfileEditorCatalog(ProfileEditorCatalog source, RemoteOfferPlacement placement)
    {
        Snapshot = source.Snapshot;
        Comparer = source.Comparer;

        _snapshotVisible = source._snapshotVisible;
        _snapshotKnown = source._snapshotKnown;
        _snapshotIndex = source._snapshotIndex;

        // The snapshot's own record wins: an offer never stands in for a version already here.
        _visible = new Dictionary<ModVersionIdentity, CatalogModVersion>(_snapshotVisible);
        _known = new Dictionary<ModVersionIdentity, CatalogModVersion>(_snapshotKnown);

        foreach (var offer in placement.Visible)
        {
            _visible.TryAdd(offer.Identity, offer);
        }

        foreach (var offer in placement.Known)
        {
            _known.TryAdd(offer.Identity, offer);
        }

        Index = placement.Index;
    }


    public static ProfileEditorCatalog Empty(IModVersionComparer comparer) => new(new ModCatalogSnapshot([], [], []), comparer);


    public ModCatalogSnapshot Snapshot { get; }
    public IModVersionComparer Comparer { get; }
    public IReadOnlyDictionary<ModKey, ModVersionSet> Index { get; }

    public IEnumerable<CatalogModVersion> Visible => _visible.Values;


    /// <summary>The same snapshot, with these remote answers placed on it instead of any before.</summary>
    public ProfileEditorCatalog WithRemote(IReadOnlyList<RemoteSourceAnswers> answers, Func<Uri, RemoteDownloadNote?> notes)
        => new(this, RemoteModOffers.Place(_snapshotIndex, answers, Comparer, notes));

    public bool IsVisible(CatalogModVersion version) => _visible.ContainsKey(version.Identity);

    /// <summary>
    /// The visible record where there is one, since it names only enabled sources; the known one
    /// otherwise; and a stand-in for a version this catalog has never heard of.
    /// </summary>
    public CatalogModVersion Resolve(ModVersionIdentity identity)
        => _visible.GetValueOrDefault(identity)
        ?? _known.GetValueOrDefault(identity)
        ?? Placeholder(identity);

    /// <summary>
    /// The pins whose version nothing but <paramref name="source"/> can supply: not registered, and
    /// found nowhere else. Unloading that source leaves them without a file to import.
    /// </summary>
    public IReadOnlyList<ModKey> PinsOnlyIn(ProfileDraft draft, ModSourceId source)
        => [.. draft.Pins.Values
            .Select(x => _known.GetValueOrDefault(new ModVersionIdentity(x.ModId, x.VersionId)))
            .OfType<CatalogModVersion>()
            .Where(x => x.IsOnServer is false
                && x.FoundIn.Count > 0
                && x.FoundIn.All(occurrence => occurrence.Source.Id == source))
            .Select(x => x.ModId)];


    /// <summary>
    /// Registered, as far as anything here can tell: the catalog's registered half is read once per
    /// page, so a version a teammate registered and pinned since is the one way to get here.
    /// </summary>
    private static CatalogModVersion Placeholder(ModVersionIdentity identity)
        => new(identity.ModId, identity.VersionId, identity.ModId.Value, string.Empty, IsLocal: false, IsOnServer: true, Locked: false);

    private static Dictionary<ModVersionIdentity, CatalogModVersion> Deduplicate(IEnumerable<CatalogModVersion> versions)
    {
        var result = new Dictionary<ModVersionIdentity, CatalogModVersion>();

        foreach (var version in versions)
        {
            result.TryAdd(version.Identity, version);
        }

        return result;
    }
}
