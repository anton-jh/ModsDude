using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Savegames;

/// <summary>
/// A named savegame inside a repo. What it holds lives on its <see cref="SavegameVersion"/>s; this
/// row holds the identity, the name, which profile it follows, whether it still follows it, and
/// which version is current.
/// </summary>
/// <remarks>
/// <para>
/// <b>A savegame is not owned by a profile.</b> It sits in the repo beside profiles, keyed
/// <c>(RepoId, Id)</c>, and each <em>version</em> records the one profile revision it was played on.
/// A save moves from revision 6 to revision 7 as the group updates its mods, so pinning a revision
/// on the savegame itself would either forbid that or lie about it.
/// </para>
/// <para>
/// <b>The profile is fixed at publish.</b> Nothing moves a savegame onto another one - a move would
/// put this row and every version's <see cref="SavegameVersion.ProfileId"/> in disagreement, and two
/// profiles' revision numbers are not comparable anyway. Somebody who wants the effect republishes
/// the savegame, which is three operations that already exist; see
/// docs/10-savegame-profile-binding.md#cardinality.
/// </para>
/// <para>
/// <b><see cref="ProfileId"/> is optional.</b> A savegame that has none is unmanaged by the
/// publisher's choice: its versions record no revision, no profile is applied when it is checked
/// out, and it is neither current nor past. Adapters with savegame support and no mod support have
/// no profile to offer, and a save in a mod-capable repo may equally be published without one.
/// </para>
/// <para>
/// As with <see cref="Profile"/>, there is no navigation to the versions. A savegame's history is
/// read through its own set, and this row only ever says which version is current.
/// </para>
/// </remarks>
public class Savegame : IArchivable
{
    // ef
    private Savegame() { }

    public Savegame(
        RepoId repoId,
        SavegameName name,
        ProfileId? profileId,
        DateTime created)
    {
        RepoId = repoId;
        Name = name;
        ProfileId = profileId;
        Created = created;
    }


    public SavegameId Id { get; init; } = new(Guid.NewGuid());
    public RepoId RepoId { get; private set; }

    public SavegameName Name { get; set; }

    /// <summary>
    /// The profile this save follows, or <c>null</c> where it follows none. Decided when the save is
    /// published and never after - see the remarks on the type.
    /// </summary>
    public ProfileId? ProfileId { get; private set; }

    public DateTime Created { get; private set; }

    /// <summary>
    /// The version a read means when it does not say, and the only one a check-in may produce a
    /// successor to. <see cref="SavegameVersionNumber.None"/> until the savegame is given its first
    /// version, which happens in the same transaction that publishes it.
    /// </summary>
    public SavegameVersionNumber HeadVersion { get; private set; } = SavegameVersionNumber.None;

    /// <summary>
    /// When this stopped being its profile's current savegame, or <c>null</c> while it still is.
    /// </summary>
    /// <remarks>
    /// <b>A different fact from <see cref="ArchivedAt"/>.</b> Archived is the repo-wide visibility
    /// state every <see cref="IArchivable"/> carries; this says which savegame a profile is following
    /// now. A savegame can be current or past, archived or not, in any combination - which is why
    /// the one-current-savegame index is deliberately not filtered on <see cref="ArchivedAt"/> the
    /// way the name index beside it is. An archived savegame still holds its profile's slot, and a
    /// second one taking that slot behind its back is exactly what the index exists to refuse.
    /// </remarks>
    public DateTime? SupersededAt { get; private set; }

    /// <inheritdoc cref="IArchivable.ArchivedAt"/>
    public DateTime? ArchivedAt { get; private set; }

    public bool IsArchived => ArchivedAt is not null;

    /// <summary>
    /// Whether this is the savegame its profile is following. Both this and <see cref="IsPast"/> are
    /// false for a savegame with no profile: it is not in a succession, so neither word applies to
    /// it.
    /// </summary>
    public bool IsCurrent => ProfileId is not null && SupersededAt is null;

    /// <inheritdoc cref="IsCurrent"/>
    public bool IsPast => ProfileId is not null && SupersededAt is not null;


    /// <summary>
    /// Puts the savegame away. Its versions and its claim log stay exactly as they were - archiving
    /// a shared save must not quietly release somebody's hold on it. Idempotent, and it does not
    /// restamp.
    /// </summary>
    public void Archive(DateTime now)
    {
        ArchivedAt ??= now;
    }

    /// <summary>
    /// Brings it back, optionally under a new name - which is how a clash with a live savegame is
    /// resolved, since an archived one gave up its name when it was archived.
    /// </summary>
    public void Restore(SavegameName? name = null)
    {
        if (name is SavegameName renamed)
        {
            Name = renamed;
        }

        ArchivedAt = null;
    }


    /// <summary>
    /// Makes this a past savegame. Its profile is following some other savegame from now on, and this
    /// one stays on the revision it was last played on rather than moving with the mod list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Only ever half of a swap.</b> Nothing supersedes a savegame on its own, because that would
    /// leave the profile with no current savegame - a state only deleting the current savegame is meant
    /// to reach. Whoever calls this is the same caller that puts something else in the slot, in the
    /// same transaction.
    /// </para>
    /// <para>
    /// Idempotent and it does not restamp, like <see cref="Archive"/>: the stamp says when the
    /// profile moved on, and a second caller saying so did not move it.
    /// </para>
    /// </remarks>
    public void Supersede(DateTime now)
    {
        RequireAProfile("superseded");

        SupersededAt ??= now;
    }

    /// <summary>
    /// Makes this its profile's current savegame again, so it follows the mod list from here.
    /// </summary>
    /// <remarks>
    /// The other half of the same swap, and the incumbent has to be superseded <b>before</b> this
    /// runs. One current savegame per profile is a filtered unique index, which refuses the instant
    /// where two rows claim the slot; ordering two updates is not something a change tracker
    /// promises, so the caller writes them as two commits inside one transaction.
    /// </remarks>
    public void MakeCurrent()
    {
        RequireAProfile("made current");

        SupersededAt = null;
    }


    /// <summary>
    /// Records <paramref name="contentHash"/> as the savegame's new head.
    /// </summary>
    /// <param name="profileRevision">
    /// The revision of the savegame's profile this version was played on, or <c>null</c> where the
    /// savegame follows no profile. Every version that has one names exactly one, which is what makes
    /// a save reproducible - and what lets the client say that a folder is on a mod list this save
    /// was never played against.
    /// </param>
    /// <param name="baseVersion">
    /// What the uploader was holding. Equal to the previous head for an ordinary check-in; the
    /// version being copied forward for <see cref="SavegameVersionOrigin.Restored"/>; and what was
    /// actually played for <see cref="SavegameVersionOrigin.Forced"/>, which is the whole point of
    /// recording it - a forced check-in leaves the fork in the record without anybody needing a tree.
    /// </param>
    /// <remarks>
    /// <para>
    /// The one way a savegame's contents ever change, and the same call behind all three things that
    /// change them: publishing, checking in, and restoring an older version. They differ only in
    /// where the bytes came from, which is what <paramref name="origin"/> records.
    /// </para>
    /// <para>
    /// <b>The version's profile is taken from the savegame rather than named beside it.</b> Nothing
    /// moves a save between profiles, so a caller that could pass one would only ever be able to
    /// disagree with this row - and a history mixing versions that record a revision with versions
    /// that do not could then arise, which the whole pairing exists to prevent. The half-set pair is
    /// refused here and by a check constraint in the database.
    /// </para>
    /// </remarks>
    public SavegameVersion CreateVersion(
        RevisionNumber? profileRevision,
        string contentHash,
        long sizeBytes,
        UserId createdBy,
        DateTime now,
        string? label = null,
        SavegameVersionOrigin origin = SavegameVersionOrigin.CheckedIn,
        SavegameVersionNumber? baseVersion = null,
        SavegameCheckoutId? checkoutId = null,
        IEnumerable<SavegameDetail>? details = null)
    {
        if (ProfileId is null && profileRevision is not null)
        {
            throw new DomainValidationException(
                $"Savegame '{Id.Value}' follows no mod list, so a version of it cannot name a revision of one.");
        }

        if (ProfileId is not null && profileRevision is null)
        {
            throw new DomainValidationException(
                $"Savegame '{Id.Value}' follows a mod list, so every version of it has to name the revision it was played on.");
        }

        var number = HeadVersion.Next();

        var version = new SavegameVersion(
            RepoId,
            Id,
            number,
            ProfileId,
            profileRevision,
            contentHash,
            sizeBytes,
            createdBy,
            now,
            label,
            origin,
            baseVersion,
            checkoutId,
            details);

        HeadVersion = number;

        return version;
    }


    private void RequireAProfile(string action)
    {
        if (ProfileId is null)
        {
            throw new InvalidOperationException(
                $"Savegame '{Id.Value}' follows no mod list, so it is neither current nor past and cannot be {action}.");
        }
    }
}


public readonly record struct SavegameId(Guid Value);


/// <summary>
/// A savegame's name, unique within its repo. It is read by people picking one to play, so the only
/// thing worth refusing is an empty one and an essay.
/// </summary>
public readonly record struct SavegameName
{
    public const int MaximumLength = 100;


    public SavegameName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException("A savegame must have a name.");
        }

        if (value.Length > MaximumLength)
        {
            throw new DomainValidationException($"A savegame name cannot be longer than {MaximumLength} characters.");
        }

        Value = value.Trim();
    }


    public string Value { get; }

    public override string ToString() => Value;
}


/// <summary>
/// Where a version sits in its savegame's history. One-based, and <b>not</b> contiguous: pruning
/// deletes old versions and leaves the gap, because numbers exist to be said out loud and
/// renumbering would make yesterday's sentence point somewhere else.
/// </summary>
public readonly record struct SavegameVersionNumber(int Value) : IComparable<SavegameVersionNumber>
{
    /// <summary>
    /// What a savegame's head is between its construction and its first version - a state that only
    /// exists inside the transaction that publishes it, and that never reaches the database.
    /// </summary>
    public static SavegameVersionNumber None { get; } = new(0);

    public SavegameVersionNumber Next() => new(Value + 1);

    public int CompareTo(SavegameVersionNumber other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString();
}
