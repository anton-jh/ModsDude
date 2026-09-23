using ModsDude.Server.Domain.Retention;
using ModsDude.Server.Domain.Savegames;

namespace ModsDude.Server.Api.Dtos;

/// <summary>
/// One savegame as the list renders it: what it is, which profile it follows, where its head stands,
/// and who has it.
/// </summary>
/// <remarks>
/// The head snapshot is carried inline because every row needs it and a list of ten savegames should
/// not be ten follow-up requests. The history behind it is read through
/// <c>GET repos/{repoId}/savegames/{savegameId}/snapshots</c>.
/// </remarks>
/// <param name="ProfileId">
/// The profile this save follows, or <c>null</c> where it follows none. Decided when the save was
/// published and never after, so it agrees with every snapshot's own profile by construction.
/// </param>
/// <param name="SupersededAt">
/// When the profile stopped following this savegame, or <c>null</c> while it still does. Null for a
/// savegame with no profile too, which is neither current nor past - the client reads the pair, not
/// this field alone.
/// <para>
/// A different fact from <paramref name="ArchivedAt"/>, and carried separately for that reason: a
/// savegame can be current or past, archived or not, in any combination, and a profile whose current
/// savegame is archived still has a current savegame.
/// </para>
/// </param>
/// <param name="Checkout">
/// The open claim, or <c>null</c> where nobody holds it. A claim is held from the moment it is taken until it ends, however long that
/// is, so "Anton has had this since March" is a claim like any other and is the thing the next person
/// needs to read.
/// </param>
/// <param name="SnapshotCount">How many snapshots the savegame has now - pruning is what takes it down.</param>
/// <param name="TotalSizeBytes">
/// What storage holds for them, counted per blob: snapshots with the same content hash are one blob - a
/// restore copies an old snapshot forward under the hash it already had - and are counted once.
/// </param>
public record SavegameDto(
    Guid Id,
    Guid RepoId,
    string Name,
    Guid? ProfileId,
    DateTime Created,
    SavegameSnapshotDto? Head,
    SavegameCheckoutDto? Checkout,
    DateTime? SupersededAt,
    DateTime? ArchivedAt,
    int SnapshotCount,
    long TotalSizeBytes);


/// <summary>
/// One snapshot in a savegame's history.
/// </summary>
/// <param name="ProfileRevision">
/// The revision of <paramref name="ProfileId"/> this snapshot was played on. It is what lets a client
/// say that a mod folder is on a list this save has never seen, which is the one kind of drift no
/// directory listing could find.
/// <para>
/// Null exactly when <paramref name="ProfileId"/> is - the two are set together or not at all, and
/// both are null for a savegame that follows no mod list. Nothing about revisions applies to one.
/// </para>
/// </param>
/// <param name="ContentHash">
/// SHA-256 of the packed save, and the address its blob is stored at. The client needs it to ask for
/// a download link, and to tell whether what is in a slot is still what was checked in.
/// </param>
/// <param name="BaseSnapshot">
/// What the uploader was holding. For <see cref="SavegameSnapshotOrigin.Forced"/> it names the
/// snapshot somebody's play was built on but did not follow, which is how a fork stays in the record
/// without a tree; for <see cref="SavegameSnapshotOrigin.Restored"/> it names what was copied forward.
/// </param>
/// <param name="CheckoutId">
/// The claim this snapshot was checked in against, which is what joins snapshots and checkouts into
/// one timeline. Null for a publish and for a forced check-in made without holding the save.
/// </param>
public record SavegameSnapshotDto(
    Guid RepoId,
    Guid SavegameId,
    int Number,
    Guid? ProfileId,
    int? ProfileRevision,
    string ContentHash,
    long SizeBytes,
    DateTime Created,
    UserDto CreatedBy,
    string? Label,
    SavegameSnapshotOrigin Origin,
    int? BaseSnapshot,
    Guid? CheckoutId,
    IEnumerable<SavegameDetailDto> Details,
    DateOnly? DeletionScheduledFor,
    DeletionReason? DeletionReason);


/// <summary>
/// One thing a client's game adapter chose to say about a snapshot - the map, when it was played,
/// how long for. <b>The server never parses one</b>; see <c>SavegameDetail</c>.
/// </summary>
/// <param name="Key">Stable and machine-readable. Never rendered - it exists so a fact can be found again later.</param>
/// <param name="Label">What to print beside the value. Prose, and safe to reword.</param>
public record SavegameDetailDto(string Key, string Label, string Value);


/// <summary>
/// One claim on a savegame, open or closed.
/// </summary>
/// <param name="Status">
/// Held while the claim is open and ended once it is not. Derived on the server so that every client
/// reads the two the same way.
/// </param>
/// <param name="EndedReason">
/// Null while the claim is open. Nothing closes a claim by itself: it ends when its holder checks in
/// or discards, or when somebody takes it over.
/// </param>
public record SavegameCheckoutDto(
    Guid Id,
    Guid RepoId,
    Guid SavegameId,
    UserDto User,
    DateTime TakenAt,
    DateTime? EndedAt,
    SavegameCheckoutEndReason? EndedReason,
    SavegameCheckoutStatus Status)
{
    public static SavegameCheckoutDto FromModel(SavegameCheckout checkout, UserDto user)
    {
        return new(
            checkout.Id.Value,
            checkout.RepoId.Value,
            checkout.SavegameId.Value,
            user,
            checkout.TakenAt,
            checkout.EndedAt,
            checkout.EndedReason,
            checkout.Status);
    }
}
