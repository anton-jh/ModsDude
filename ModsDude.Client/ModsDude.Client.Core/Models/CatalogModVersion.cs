using ModsDude.Client.Core.Imagery;
using ModsDude.Client.Core.ModsDudeServer.Generated;

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
    /// <summary>
    /// A version the repo holds, with no local half at all - the shape for a reader who is never
    /// going to import anything. The registered record is the whole truth for one of those, so not
    /// paying for a disk scan to learn it is the point.
    /// </summary>
    public static CatalogModVersion FromRegistered(ModDto dto)
    {
        return new CatalogModVersion(
            ModKey.From(dto.ModId),
            ModVersionKey.From(dto.VersionId),
            dto.DisplayName,
            dto.Description,
            IsLocal: false,
            IsOnServer: true,
            Locked: dto.Locked)
        {
            ServerImages = [.. dto.Images.Select(ModImageReference.FromDto)],
            ContentHash = dto.ContentHash,
            SequenceNumber = dto.SequenceNumber
        };
    }


    public ModVersionIdentity Identity => new(ModId, VersionId);

    public string? Author { get; init; }

    /// <summary>
    /// The archive's own icon, for a version that has a file here. Not what a registered version
    /// renders - see <see cref="ServerImages"/> - but what its derivatives are generated from.
    /// </summary>
    public ModImage? Icon { get; init; }

    /// <inheritdoc cref="Icon"/>
    public IReadOnlyList<ModImage> Images { get; init; } = [];

    /// <summary>
    /// The derivatives the repo holds for this version. Empty until imagery has been published,
    /// which happens after registration and never blocks it.
    /// </summary>
    /// <remarks>
    /// A registered version renders from these and not from the archive, even when the file is on
    /// this machine. Hunting for the local file would cost a per-row archive open and a managed BC7
    /// decode to gain resolution nobody is looking for in a 96 px strip - exactly the work
    /// derivatives exist to avoid - and it is not even faster after the first fetch, since a
    /// content-addressed image crosses the wire once per machine ever. It also means the content
    /// store is never an image source. See docs/09-mod-catalog.md#registration-decides-where-imagery-comes-from.
    /// </remarks>
    public IReadOnlyList<ModImageReference> ServerImages { get; init; } = [];

    /// <summary>Every source this version turned up in. Empty for a version only the repo has.</summary>
    public IReadOnlyList<ModOccurrence> FoundIn { get; init; } = [];

    /// <summary>The registered file's hash, and null until it is registered.</summary>
    public string? ContentHash { get; init; }

    /// <summary>Where the repo orders this version among its siblings. Null until registered.</summary>
    public int? SequenceNumber { get; init; }

    /// <summary>
    /// How many of the repo's profiles pin this version. Null for a version the repo does not hold,
    /// which has no dependency that could name it.
    /// </summary>
    /// <remarks>
    /// Comes from the server rather than from whichever profiles this client happens to have loaded:
    /// dependencies arrive one profile at a time, and deleting on a partial view risks removing a
    /// version a teammate's profile just picked up. Advisory even so - the delete endpoints re-ask
    /// the database at the moment it matters. See docs/09-mod-catalog.md#manage.
    /// </remarks>
    public int? UsedByProfiles { get; init; }

    /// <summary>Registered, and nothing in the repo depends on it - so a delete would be accepted.</summary>
    public bool IsUnused => IsOnServer && UsedByProfiles is 0;

    /// <summary>
    /// A cheap warning that this version's sources may disagree: they hold files of different sizes,
    /// which is typically a re-uploaded build the author did not renumber.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Advisory only, and both approximate directions are accepted.</b> It over-reports nothing
    /// and under-reports plenty - equal sizes are not equal bytes - because it is computed for every
    /// row of every scan and proving it would mean hashing every archive in every source.
    /// </para>
    /// <para>
    /// The authority is <see cref="Import.ModOccurrenceResolver"/>, which hashes, and it runs at
    /// import over the handful of versions actually selected. A row with no chip can still raise the
    /// question, and a row with one can turn out to have identical copies and never ask.
    /// </para>
    /// </remarks>
    public bool HasSourceConflict => FoundIn.Select(x => x.FileLength).Distinct().Count() > 1;

    /// <summary>
    /// The bytes to read for anything that is not an import - imagery, mostly. Null for a version
    /// with no local file, and null while <see cref="HasSourceConflict"/> stands, because a caller
    /// with no way to ask cannot be handed one of two files at random.
    /// </summary>
    /// <remarks>
    /// <b>The import does not read this.</b> It resolves the sources properly and reads the
    /// occurrence it chose, which is the only path that can pick between two differing files.
    /// </remarks>
    public Func<Stream>? OpenStream => HasSourceConflict
        ? null
        : FoundIn.FirstOrDefault()?.OpenStream;

    /// <summary>
    /// What a file here is called, on the same terms as <see cref="OpenStream"/> and for the same
    /// reason: a name has to describe the bytes it is recorded against.
    /// </summary>
    public ModFileName? FileName => HasSourceConflict || FoundIn.FirstOrDefault() is not ModOccurrence source
        ? null
        : ModFileName.ForFile(ModId, source.FilePath);
}

/// <summary>One source's copy of a version, and the file it found there.</summary>
public record ModOccurrence(ModSource Source, string FilePath, long FileLength, Func<Stream> OpenStream);
