using ModsDude.Server.Domain.Savegames;

namespace ModsDude.Server.Api.Dtos;

/// <summary>
/// One savegame as the list renders it: what it is, which profile it follows, where its head stands,
/// and who has it.
/// </summary>
/// <remarks>
/// The head version is carried inline because every row needs it and a list of ten savegames should
/// not be ten follow-up requests. The history behind it is read through
/// <c>GET repos/{repoId}/savegames/{savegameId}/versions</c>.
/// </remarks>
/// <param name="ProfileId">
/// The profile this save follows - the standing intent, which is a different fact from the revision
/// its head version was played on. The two may legitimately disagree once somebody moves a save onto
/// a branched profile.
/// </param>
/// <param name="Checkout">
/// The open claim, or <c>null</c> where nobody holds it. A claim past its expiry is still reported -
/// with <see cref="SavegameCheckoutStatus.Stale"/> - rather than omitted, because "Anton has had
/// this since March" is the thing the next person needs to read.
/// </param>
public record SavegameDto(
    Guid Id,
    Guid RepoId,
    string Name,
    Guid ProfileId,
    DateTime Created,
    SavegameVersionDto? Head,
    SavegameCheckoutDto? Checkout,
    DateTime? ArchivedAt);


/// <summary>
/// One version in a savegame's history.
/// </summary>
/// <param name="ProfileRevision">
/// The revision of <paramref name="ProfileId"/> this version was played on. It is what lets a client
/// say that a mod folder is on a list this save has never seen, which is the one kind of drift no
/// directory listing could find.
/// </param>
/// <param name="ContentHash">
/// SHA-256 of the packed save, and the address its blob is stored at. The client needs it to ask for
/// a download link, and to tell whether what is in a slot is still what was checked in.
/// </param>
/// <param name="BaseVersion">
/// What the uploader was holding. For <see cref="SavegameVersionOrigin.Forced"/> it names the
/// version somebody's play was built on but did not follow, which is how a fork stays in the record
/// without a tree; for <see cref="SavegameVersionOrigin.Restored"/> it names what was copied forward.
/// </param>
/// <param name="CheckoutId">
/// The claim this version was checked in against, which is what joins versions and checkouts into
/// one timeline. Null for a publish and for a forced check-in made without holding the save.
/// </param>
public record SavegameVersionDto(
    Guid RepoId,
    Guid SavegameId,
    int Number,
    Guid ProfileId,
    int ProfileRevision,
    string ContentHash,
    long SizeBytes,
    DateTime Created,
    UserDto CreatedBy,
    string? Label,
    SavegameVersionOrigin Origin,
    int? BaseVersion,
    Guid? CheckoutId,
    IEnumerable<SavegameDetailDto> Details);


/// <summary>
/// One thing a client's game adapter chose to say about a version - the map, when it was played,
/// how long for. <b>The server never parses one</b>; see <c>SavegameDetail</c>.
/// </summary>
/// <param name="Key">Stable and machine-readable. Never rendered - it exists so a fact can be found again later.</param>
/// <param name="Label">What to print beside the value. Prose, and safe to reword.</param>
public record SavegameDetailDto(string Key, string Label, string Value);


/// <summary>
/// One claim on a savegame, open or closed.
/// </summary>
/// <param name="Status">
/// Folded server-side so that every client tells a live claim from a forgotten one the same way, and
/// so that "held" cannot drift apart from the expiry it is derived from.
/// </param>
/// <param name="EndedReason">
/// Null while the claim is open. Expiry is not among the reasons: nothing closes a row when it
/// lapses, so an expired claim is an open row reading <see cref="SavegameCheckoutStatus.Stale"/>.
/// </param>
public record SavegameCheckoutDto(
    Guid Id,
    Guid RepoId,
    Guid SavegameId,
    UserDto User,
    DateTime TakenAt,
    DateTime ExpiresAt,
    DateTime? EndedAt,
    SavegameCheckoutEndReason? EndedReason,
    SavegameCheckoutStatus Status)
{
    public static SavegameCheckoutDto FromModel(SavegameCheckout checkout, UserDto user, DateTime now)
    {
        return new(
            checkout.Id.Value,
            checkout.RepoId.Value,
            checkout.SavegameId.Value,
            user,
            checkout.TakenAt,
            checkout.ExpiresAt,
            checkout.EndedAt,
            checkout.EndedReason,
            checkout.GetStatus(now));
    }
}
