using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Profiles.Editor;

/// <summary>
/// A savegame on this machine, read as a source: the mods it lists, each found in the save's own mod
/// folder, the repo or a remote source. It holds no files of its own.
/// </summary>
/// <remarks>
/// Matching looks at every version the catalog knows, whether or not the chip it came from is on, so
/// switching the mod folder off to see only the save does not lose the files the save is found in.
/// </remarks>
public sealed class SavegameModSource
{
    private readonly HashSet<ModKey> _listed;


    /// <param name="modFolder">The mod folder the save is played against.</param>
    public SavegameModSource(ModSource source, ModSourceId modFolder, IReadOnlyList<SavegameModReference> mods)
    {
        Source = source;
        ModFolder = modFolder;
        Mods = [.. mods.OrderBy(x => x.ModId.Value, StringComparer.Ordinal)];

        _listed = [.. mods.Select(x => x.ModId)];
    }


    public ModSource Source { get; }
    public ModSourceId ModFolder { get; }

    /// <summary>Sorted by mod id.</summary>
    public IReadOnlyList<SavegameModReference> Mods { get; }

    public bool Lists(ModKey modId) => _listed.Contains(modId);

    /// <summary>
    /// Every version of each listed mod that the repo holds, a remote source offers or the save's mod
    /// folder has, and the one to pick by default: the version the save recorded, else the one in its
    /// mod folder, else none, which leaves the editor's own default.
    /// </summary>
    public SavegameModMatches Match(ProfileEditorCatalog catalog)
    {
        var found = new List<SavegameModMatch>();
        var notFound = new List<ModKey>();

        foreach (var mod in Mods)
        {
            var candidates = catalog.Index.GetValueOrDefault(mod.ModId)?.Order.Where(IsCandidate).ToList() ?? [];

            if (candidates.Count == 0)
            {
                notFound.Add(mod.ModId);

                continue;
            }

            var preferred = candidates.FirstOrDefault(x => x.VersionId == mod.Version)
                ?? candidates.LastOrDefault(IsInModFolder);

            found.Add(new SavegameModMatch(mod.ModId, [.. candidates.Select(x => x.VersionId)], preferred?.VersionId));
        }

        return new SavegameModMatches(found, notFound);
    }

    /// <summary>
    /// How many mods the save lists, where the version picked by default comes from for each found
    /// one, and which were not found.
    /// </summary>
    public SavegameSourceSummary Summarize(ProfileEditorCatalog catalog)
    {
        var matches = Match(catalog);

        var byLocation = matches.Found
            .Select(x => ProfileEditorState.Location(catalog.Resolve(new ModVersionIdentity(x.ModId, x.Preferred ?? x.Versions[^1]))))
            .GroupBy(x => x)
            .Select(x => new SavegameModLocationCount(x.Key, x.Count()))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Location, StringComparer.Ordinal)
            .ToList();

        return new SavegameSourceSummary(Mods.Count, byLocation, matches.NotFound);
    }


    private bool IsCandidate(CatalogModVersion version)
        => version.IsOnServer || version.Download is not null || IsInModFolder(version);

    private bool IsInModFolder(CatalogModVersion version) => version.FoundIn.Any(x => x.Source.Id == ModFolder);
}


/// <param name="Found">Sorted by mod id.</param>
/// <param name="NotFound">Listed mods no version was found of, sorted by mod id.</param>
public sealed record SavegameModMatches(IReadOnlyList<SavegameModMatch> Found, IReadOnlyList<ModKey> NotFound);


/// <param name="Versions">Oldest first.</param>
/// <param name="Preferred">The version to pick by default, or null for the editor's own default.</param>
public sealed record SavegameModMatch(ModKey ModId, IReadOnlyList<ModVersionKey> Versions, ModVersionKey? Preferred);


/// <param name="ByLocation">Most first, then by name.</param>
/// <param name="NotFound">Sorted by mod id.</param>
public sealed record SavegameSourceSummary(int Listed, IReadOnlyList<SavegameModLocationCount> ByLocation, IReadOnlyList<ModKey> NotFound);


public sealed record SavegameModLocationCount(string Location, int Count);
