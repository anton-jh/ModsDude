namespace ModsDude.Client.Core.Models;

/// <summary>
/// The join key between what is on disk and what the repo has registered. Exact, not fuzzy: an
/// archive's filename stem and its modDesc version are literally what registration stores.
/// </summary>
public readonly record struct ModVersionIdentity(ModKey ModId, ModVersionKey VersionId);

/// <summary>
/// One mod version as the catalog sees it, wherever it happens to live. Flat - one record per
/// version, with no parent - because everything either side of it is: the server entity is one row
/// per version, a scan yields one record per file, and a row view model wraps exactly one version.
/// </summary>
/// <remarks>
/// "Local only / server only / both" is one identity with two independent facts rather than three
/// kinds of mod, so the three-state value is derived where a page needs it and never stored.
/// Grouping - the version selector, and whether a newer version exists - is a
/// <c>ToLookup(x =&gt; x.ModId)</c> built where needed, not a nested model that would have to be
/// rebuilt every time a source checkbox recomposes the set.
/// See docs/09-mod-catalog.md#a-merged-model.
/// </remarks>
public record CatalogModVersion(
    ModKey ModId,
    ModVersionKey VersionId,
    string Name,
    string Description,
    bool IsLocal,
    bool IsOnServer,
    bool Locked)
{
    public ModVersionIdentity Identity => new(ModId, VersionId);

    public string? Author { get; init; }

    public ModImage? Icon { get; init; }

    public IReadOnlyList<ModImage> Images { get; init; } = [];

    /// <summary>Every source this version turned up in. Empty for a version only the repo has.</summary>
    public IReadOnlyList<ModOccurrence> FoundIn { get; init; } = [];

    /// <summary>The registered file's hash, and null until it is registered.</summary>
    public string? ContentHash { get; init; }

    /// <summary>Where the repo orders this version among its siblings. Null until registered.</summary>
    public int? SequenceNumber { get; init; }

    /// <summary>
    /// True when two sources hold a file claiming this mod and version but disagreeing on its size -
    /// typically a re-uploaded build the author did not renumber. Only one can ever be registered,
    /// so the user picks the source rather than the catalog picking silently.
    /// </summary>
    /// <remarks>
    /// Under-reports rather than over-reports: equal sizes are not proof of equal bytes, and proving
    /// it would mean hashing every archive in every source on every scan.
    /// </remarks>
    public bool HasSourceConflict => FoundIn.Select(x => x.FileLength).Distinct().Count() > 1;

    /// <summary>
    /// The bytes to import, when the sources agree on which bytes those are. Null for a version with
    /// no local file, and null while <see cref="HasSourceConflict"/> stands - resolving that is the
    /// user's choice from <see cref="FoundIn"/>, not this record's.
    /// </summary>
    public Func<Stream>? OpenStream => HasSourceConflict
        ? null
        : FoundIn.FirstOrDefault()?.OpenStream;
}

/// <summary>One source's copy of a version, and the file it found there.</summary>
public record ModOccurrence(ModSource Source, string FilePath, long FileLength, Func<Stream> OpenStream);
