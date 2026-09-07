using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Savegames;

/// <summary>
/// One immutable version of a savegame - the bytes somebody checked in, and the mod list they were
/// played against.
/// </summary>
/// <remarks>
/// <para>
/// Read-only for the same reason a profile revision is: <b>nothing addresses one to write to it</b>.
/// A check-in produces a successor, a restore copies an old one forward, and neither reopens
/// anything. There is no flag anybody has to remember to check.
/// </para>
/// <para>
/// <b><see cref="ProfileRevision"/> is never optional.</b> A version that could not say which mod
/// list it was played on would make the one warning that matters - your folder is on a list this
/// save has never seen - unanswerable. A repo whose adapter has no mods still has one implicit
/// profile with one revision, so the column stays honest rather than nullable; see
/// docs/PLAN.md#phase-8--savegames.
/// </para>
/// </remarks>
public class SavegameVersion
{
    /// <summary>
    /// A label is read by people scanning a history, not matched on, so this only has to stop
    /// somebody pasting an essay into it. It is also the flag that exempts a version from pruning.
    /// </summary>
    public const int MaximumLabelLength = 100;


    // ef
    private SavegameVersion() { }

    /// <summary>
    /// Reached through <see cref="Savegame.CreateVersion"/>, which is what decides the number and
    /// moves the head. Constructing one directly would let a caller invent a number.
    /// </summary>
    internal SavegameVersion(
        RepoId repoId,
        SavegameId savegameId,
        SavegameVersionNumber number,
        ProfileId profileId,
        RevisionNumber profileRevision,
        string contentHash,
        long sizeBytes,
        UserId createdBy,
        DateTime created,
        string? label,
        SavegameVersionOrigin origin,
        SavegameVersionNumber? baseVersion,
        SavegameCheckoutId? checkoutId,
        IEnumerable<SavegameDetail>? details = null)
    {
        if (label is { Length: > MaximumLabelLength })
        {
            throw new DomainValidationException($"A savegame version label cannot be longer than {MaximumLabelLength} characters.");
        }

        if (sizeBytes <= 0)
        {
            throw new DomainValidationException($"A savegame version cannot be {sizeBytes} bytes.");
        }

        RepoId = repoId;
        SavegameId = savegameId;
        Number = number;
        ProfileId = profileId;
        ProfileRevision = profileRevision;
        // Validated rather than taken on trust: the hash is a blob path segment, so anything that is
        // not one addresses something that is not a savegame.
        ContentHash = ModImageHash.Validated(contentHash);
        SizeBytes = sizeBytes;
        CreatedBy = createdBy;
        Created = created;
        Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        Origin = origin;
        BaseVersion = baseVersion;
        CheckoutId = checkoutId;

        // Opaque, so there is nothing to validate beyond keeping the adapter's own ordering: the
        // server has no idea what a "map" is and is not entitled to an opinion about it.
        _details = [.. (details ?? []).OrderBy(x => x.Position)];
    }


    public RepoId RepoId { get; private set; }
    public SavegameId SavegameId { get; private set; }
    public SavegameVersionNumber Number { get; private set; }

    /// <summary>The profile whose revision this version was played on.</summary>
    public ProfileId ProfileId { get; private set; }

    /// <summary>
    /// The revision that profile was at. The foreign key onto it is <c>Restrict</c>, so a profile
    /// that has been played can no longer be deleted - the same bargain as a pinned mod version, one
    /// aggregate up.
    /// </summary>
    public RevisionNumber ProfileRevision { get; private set; }

    /// <summary>
    /// SHA-256 of the packed save, lowercase hex, and <b>the address the blob is stored at</b>
    /// within the savegame - see <see cref="Mods.BlobReclamation.TryParseSavegameBlobName"/>.
    /// Addressing by content rather than by version number is what makes two concurrent check-ins
    /// write two different blobs instead of racing for one name, and what makes a restore a pure
    /// metadata operation.
    /// </summary>
    public string ContentHash { get; private set; } = string.Empty;

    /// <summary>What the packed save weighs, so a history can say so without a storage round trip.</summary>
    public long SizeBytes { get; private set; }

    public UserId CreatedBy { get; private set; }
    public DateTime Created { get; private set; }

    /// <summary>
    /// What somebody called this version, or <c>null</c> where nobody named it. <b>A named version is
    /// never pruned</b> - labelling one is how a person keeps it.
    /// </summary>
    public string? Label { get; private set; }

    public SavegameVersionOrigin Origin { get; private set; }

    /// <summary>
    /// The version this one was built on. See <see cref="Savegame.CreateVersion"/>; <c>null</c> only
    /// for the first version of a savegame, which was built on nothing.
    /// </summary>
    public SavegameVersionNumber? BaseVersion { get; private set; }

    /// <summary>
    /// The checkout this version was checked in against, which is what joins the two halves of a
    /// savegame's history into one timeline. <c>null</c> for a publish, and for a forced check-in
    /// made without holding the save.
    /// </summary>
    public SavegameCheckoutId? CheckoutId { get; private set; }

    /// <summary>
    /// What the adapter said about this version, in the order it wanted them read. Empty for a
    /// version checked in by a client whose adapter describes nothing, which is a perfectly ordinary
    /// thing for an adapter to do.
    /// </summary>
    /// <remarks>
    /// An owned collection, like <c>ModVersion.Attributes</c>, and read-only from the outside: a
    /// version is immutable, so the details of one are decided when it is minted and never after.
    /// </remarks>
    public IReadOnlyCollection<SavegameDetail> Details => _details;

    private readonly List<SavegameDetail> _details = [];
}


/// <summary>Why a version exists, which is what the history reads to describe it.</summary>
public enum SavegameVersionOrigin
{
    /// <summary>The savegame's first version, made when it was published from somebody's slot.</summary>
    Created,

    /// <summary>Somebody played the save and checked it back in.</summary>
    CheckedIn,

    /// <summary>
    /// A check-in whose base was no longer the head, made anyway. The fork it records is the reason
    /// this is a distinct origin rather than an ordinary check-in - somebody's play was superseded.
    /// </summary>
    Forced,

    /// <summary>An older version of this savegame, copied forward to the front.</summary>
    Restored
}
