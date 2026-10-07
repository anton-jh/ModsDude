using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Savegames;

/// <summary>
/// A named savegame inside a repo. What it holds lives on its <see cref="SavegameSnapshot"/>s; this
/// row holds the identity, the name, which profile it follows, and which snapshot is its head.
/// </summary>
/// <remarks>
/// <para>
/// <b>A savegame is not owned by a profile.</b> It sits in the repo beside profiles, keyed
/// <c>(RepoId, Id)</c>, and each <em>snapshot</em> records the one profile revision it was played on.
/// A save moves from revision 6 to revision 7 as the group updates its mods, so pinning a revision
/// on the savegame itself would either forbid that or lie about it.
/// </para>
/// <para>
/// <b>The profile is fixed at publish.</b> Nothing moves a savegame onto another one - a move would
/// put this row and every snapshot's <see cref="SavegameSnapshot.ProfileId"/> in disagreement, and two
/// profiles' revision numbers are not comparable anyway. Somebody who wants the effect republishes
/// the savegame, which is three operations that already exist; see
/// docs/10-savegame-profile-binding.md#cardinality.
/// </para>
/// <para>
/// <b><see cref="ProfileId"/> is optional.</b> A savegame that has none is unmanaged by the
/// publisher's choice: its snapshots record no revision, no profile is applied when it is checked
/// out. Adapters with savegame support and no mod support have
/// no profile to offer, and a save in a mod-capable repo may equally be published without one.
/// </para>
/// <para>
/// As with <see cref="Profile"/>, there is no navigation to the snapshots. A savegame's history is
/// read through its own set, and this row only ever says which snapshot is the head.
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

    public SavegameName Name { get; private set; }

    /// <summary>
    /// Counts changes to its name and to whether it is archived. A concurrency token, and what a client
    /// sends back to say which version its change was made against.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// The profile this save follows, or <c>null</c> where it follows none. Decided when the save is
    /// published and never after - see the remarks on the type.
    /// </summary>
    public ProfileId? ProfileId { get; private set; }

    public DateTime Created { get; private set; }

    /// <summary>
    /// The snapshot a read means when it does not say, and the only one a check-in may produce a
    /// successor to. <see cref="SavegameSnapshotNumber.None"/> until the savegame is given its first
    /// snapshot, which happens in the same transaction that publishes it.
    /// </summary>
    public SavegameSnapshotNumber HeadSnapshot { get; private set; } = SavegameSnapshotNumber.None;

    /// <inheritdoc cref="IArchivable.ArchivedAt"/>
    public DateTime? ArchivedAt { get; private set; }

    public bool IsArchived => ArchivedAt is not null;


    /// <summary>
    /// Puts the savegame away. Its snapshots and its claim log stay exactly as they were - archiving
    /// a shared save must not quietly release somebody's hold on it. Idempotent, and it does not
    /// restamp.
    /// </summary>
    public void Rename(SavegameName name)
    {
        if (Name == name)
        {
            return;
        }

        Name = name;
        Version++;
    }

    public void Archive(DateTime now)
    {
        if (ArchivedAt is not null)
        {
            return;
        }

        ArchivedAt = now;
        Version++;
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
        Version++;
    }


    /// <summary>
    /// Records <paramref name="contentHash"/> as the savegame's new head.
    /// </summary>
    /// <param name="profileRevision">
    /// The revision of the savegame's profile this snapshot was played on, or <c>null</c> where the
    /// savegame follows no profile. Every snapshot that has one names exactly one, which is what makes
    /// a save reproducible - and what lets the client say that a folder is on a mod list this save
    /// was never played against.
    /// </param>
    /// <param name="baseSnapshot">
    /// What the uploader was holding. Equal to the previous head for an ordinary check-in; the
    /// snapshot being copied forward for <see cref="SavegameSnapshotOrigin.Restored"/>; and what was
    /// actually played for <see cref="SavegameSnapshotOrigin.Forced"/>, which is the whole point of
    /// recording it - a forced check-in leaves the fork in the record without anybody needing a tree.
    /// </param>
    /// <remarks>
    /// <para>
    /// The one way a savegame's contents ever change, and the same call behind all three things that
    /// change them: publishing, checking in, and restoring an older snapshot. They differ only in
    /// where the bytes came from, which is what <paramref name="origin"/> records.
    /// </para>
    /// <para>
    /// <b>The snapshot's profile is taken from the savegame rather than named beside it.</b> Nothing
    /// moves a save between profiles, so a caller that could pass one would only ever be able to
    /// disagree with this row - and a history mixing snapshots that record a revision with snapshots
    /// that do not could then arise, which the whole pairing exists to prevent. The half-set pair is
    /// refused here and by a check constraint in the database.
    /// </para>
    /// </remarks>
    public SavegameSnapshot CreateSnapshot(
        RevisionNumber? profileRevision,
        string contentHash,
        long sizeBytes,
        UserId createdBy,
        DateTime now,
        string? label = null,
        SavegameSnapshotOrigin origin = SavegameSnapshotOrigin.CheckedIn,
        SavegameSnapshotNumber? baseSnapshot = null,
        SavegameCheckoutId? checkoutId = null,
        IEnumerable<SavegameDetail>? details = null)
    {
        if (ProfileId is null && profileRevision is not null)
        {
            throw new DomainValidationException(
                $"Savegame '{Id.Value}' follows no mod list, so a snapshot of it cannot name a revision of one.");
        }

        if (ProfileId is not null && profileRevision is null)
        {
            throw new DomainValidationException(
                $"Savegame '{Id.Value}' follows a mod list, so every snapshot of it has to name the revision it was played on.");
        }

        var number = HeadSnapshot.Next();

        var snapshot = new SavegameSnapshot(
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
            baseSnapshot,
            checkoutId,
            details);

        HeadSnapshot = number;

        return snapshot;
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
/// Where a snapshot sits in its savegame's history. One-based, and <b>not</b> contiguous: pruning
/// deletes old snapshots and leaves the gap, because numbers exist to be said out loud and
/// renumbering would make yesterday's sentence point somewhere else.
/// </summary>
public readonly record struct SavegameSnapshotNumber(int Value) : IComparable<SavegameSnapshotNumber>
{
    /// <summary>
    /// What a savegame's head is between its construction and its first snapshot - a state that only
    /// exists inside the transaction that publishes it, and that never reaches the database.
    /// </summary>
    public static SavegameSnapshotNumber None { get; } = new(0);

    public SavegameSnapshotNumber Next() => new(Value + 1);

    public int CompareTo(SavegameSnapshotNumber other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString();
}
