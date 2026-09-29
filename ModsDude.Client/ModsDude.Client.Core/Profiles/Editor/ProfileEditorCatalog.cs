using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModVersions;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Core.Profiles.Editor;

/// <summary>
/// One catalog snapshot, indexed once for every state computed from it.
/// </summary>
/// <remarks>
/// A version is <em>visible</em> when the repo holds it or an enabled source does: only those are
/// offered in selectors and as updates. Versions from sources on standby are still ordered and still
/// resolve the pins that point at them, so a pending pin keeps its file.
/// </remarks>
public sealed class ProfileEditorCatalog
{
    private readonly Dictionary<ModVersionIdentity, CatalogModVersion> _visible;
    private readonly Dictionary<ModVersionIdentity, CatalogModVersion> _known;


    public ProfileEditorCatalog(ModCatalogSnapshot snapshot, IModVersionComparer comparer)
    {
        Snapshot = snapshot;
        Comparer = comparer;

        _visible = Deduplicate(snapshot.Versions);
        _known = Deduplicate(snapshot.Known);

        Index = ModVersionIndex.Build(_known.Values, comparer);
    }


    public static ProfileEditorCatalog Empty(IModVersionComparer comparer) => new(new ModCatalogSnapshot([], [], []), comparer);


    public ModCatalogSnapshot Snapshot { get; }
    public IModVersionComparer Comparer { get; }
    public IReadOnlyDictionary<ModKey, ModVersionSet> Index { get; }

    public IEnumerable<CatalogModVersion> Visible => _visible.Values;


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
