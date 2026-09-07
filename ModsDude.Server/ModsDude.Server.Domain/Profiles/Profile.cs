using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Profiles;

/// <summary>
/// A named mod list inside a repo. What it pins lives on its <see cref="ProfileRevision"/>s; this
/// holds the identity, the name, and which revision is current.
/// </summary>
/// <remarks>
/// The head is a scalar rather than a collection, and there is no navigation to the revisions at
/// all. A profile's history is hundreds of thousands of dependency rows at the volumes this targets,
/// and every load of a profile - to rename it, to delete it, to check a name - would drag it in.
/// Revisions are queried through their own set; this row only ever says which one is current.
/// </remarks>
public class Profile(
    RepoId repoId,
    ProfileName name,
    DateTime created)
    : IArchivable
{
    public ProfileId Id { get; init; } = new(Guid.NewGuid());
    public RepoId RepoId { get; } = repoId;

    public ProfileName Name { get; set; } = name;
    public DateTime Created { get; } = created;

    /// <summary>
    /// The revision a read means when it does not say, and the only one a write may produce a
    /// successor to. <see cref="RevisionNumber.None"/> until the profile is given its first
    /// revision, which happens in the same transaction that creates it.
    /// </summary>
    public RevisionNumber HeadRevision { get; private set; } = RevisionNumber.None;

    /// <inheritdoc cref="IArchivable.ArchivedAt"/>
    public DateTime? ArchivedAt { get; private set; }

    public bool IsArchived => ArchivedAt is not null;


    /// <summary>
    /// Puts the profile away. It keeps its history, its revisions stay reproducible, and anything
    /// following it - an instance, a savegame - goes on following it; it is only out of the lists.
    /// Idempotent, and it does not restamp.
    /// </summary>
    public void Archive(DateTime now)
    {
        ArchivedAt ??= now;
    }

    /// <summary>
    /// Brings it back, optionally under a new name - which is how a clash with a live profile is
    /// resolved, since an archived one gave up its name when it was archived.
    /// </summary>
    public void Restore(ProfileName? name = null)
    {
        if (name is ProfileName renamed)
        {
            Name = renamed;
        }

        ArchivedAt = null;
    }


    /// <summary>
    /// Snapshots <paramref name="modDependencies"/> as the profile's new head.
    /// </summary>
    /// <param name="previousPins">
    /// What the current head pins, for the summary. The caller has it as a projection rather than as
    /// entities, which is the point - a save must not materialize the snapshot it is replacing.
    /// </param>
    /// <remarks>
    /// The one way a profile's mod list ever changes, and the same call behind all three of the
    /// things that change it: saving, restoring an older revision, and copying one into a profile
    /// branched off this one. They differ only in where the dependencies came from, which is what
    /// <paramref name="origin"/> records.
    /// </remarks>
    public ProfileRevision CreateRevision(
        IReadOnlyCollection<ModDependency> modDependencies,
        IReadOnlyCollection<ProfileModPin> previousPins,
        UserId createdBy,
        DateTime now,
        string? label = null,
        ProfileRevisionOrigin origin = ProfileRevisionOrigin.Saved,
        ProfileId? sourceProfileId = null,
        RevisionNumber? sourceRevision = null)
    {
        var number = HeadRevision.Next();

        var revision = new ProfileRevision(
            RepoId,
            Id,
            number,
            modDependencies,
            previousPins,
            createdBy,
            now,
            label,
            origin,
            sourceProfileId,
            sourceRevision);

        HeadRevision = number;

        return revision;
    }
}

public readonly record struct ProfileId(Guid Value);
public readonly record struct ProfileName(string Value);
