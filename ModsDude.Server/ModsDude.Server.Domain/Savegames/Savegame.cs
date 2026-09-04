using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Savegames;

/// <summary>
/// A named savegame inside a repo. What it holds lives on its <see cref="SavegameVersion"/>s; this
/// row holds the identity, the name, which profile it follows, and which version is current.
/// </summary>
/// <remarks>
/// <para>
/// <b>A savegame is not owned by a profile.</b> It sits in the repo beside profiles, keyed
/// <c>(RepoId, Id)</c>, and each <em>version</em> records the one profile revision it was played on.
/// A save moves from revision 6 to revision 7 as the group updates its mods, so pinning a revision
/// on the savegame itself would either forbid that or lie about it.
/// </para>
/// <para>
/// <see cref="ProfileId"/> is a different fact from the version's: it is the standing intent that
/// this save follows that profile, the distinction <c>ActiveProfile</c> draws against the sync
/// manifest one aggregate over. The two may legitimately disagree - branch a profile, move the save
/// onto the branch, and the older versions still honestly name the old profile's revisions.
/// </para>
/// <para>
/// As with <see cref="Profile"/>, there is no navigation to the versions. A savegame's history is
/// read through its own set, and this row only ever says which version is current.
/// </para>
/// </remarks>
public class Savegame
{
    // ef
    private Savegame() { }

    public Savegame(
        RepoId repoId,
        SavegameName name,
        ProfileId profileId,
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
    /// The profile this save follows. Intent rather than history - see the remarks on the type.
    /// </summary>
    public ProfileId ProfileId { get; set; }

    public DateTime Created { get; private set; }

    /// <summary>
    /// The version a read means when it does not say, and the only one a check-in may produce a
    /// successor to. <see cref="SavegameVersionNumber.None"/> until the savegame is given its first
    /// version, which happens in the same transaction that publishes it.
    /// </summary>
    public SavegameVersionNumber HeadVersion { get; private set; } = SavegameVersionNumber.None;


    /// <summary>
    /// Records <paramref name="contentHash"/> as the savegame's new head.
    /// </summary>
    /// <param name="profileRevision">
    /// The revision this version was played on. Every version names exactly one, which is what makes
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
    /// The one way a savegame's contents ever change, and the same call behind all three things that
    /// change them: publishing, checking in, and restoring an older version. They differ only in
    /// where the bytes came from, which is what <paramref name="origin"/> records.
    /// </remarks>
    public SavegameVersion CreateVersion(
        ProfileId profileId,
        RevisionNumber profileRevision,
        string contentHash,
        long sizeBytes,
        UserId createdBy,
        DateTime now,
        string? label = null,
        SavegameVersionOrigin origin = SavegameVersionOrigin.CheckedIn,
        SavegameVersionNumber? baseVersion = null,
        SavegameCheckoutId? checkoutId = null)
    {
        var number = HeadVersion.Next();

        var version = new SavegameVersion(
            RepoId,
            Id,
            number,
            profileId,
            profileRevision,
            contentHash,
            sizeBytes,
            createdBy,
            now,
            label,
            origin,
            baseVersion,
            checkoutId);

        HeadVersion = number;

        return version;
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
