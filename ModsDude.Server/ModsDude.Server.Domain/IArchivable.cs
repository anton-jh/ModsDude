namespace ModsDude.Server.Domain;

/// <summary>
/// Something that is put away rather than destroyed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Repos, profiles and savegames are never deleted directly.</b> Each is the thing a group's
/// shared work hangs off, and each takes something irreplaceable with it - a profile's history, a
/// savegame's backups, a repo's whole catalog. Archiving is the only way to make one go away, and
/// permanently deleting it is a second, deliberate act taken from the archive by an admin.
/// </para>
/// <para>
/// <b>Archiving changes exactly two things: visibility, and the name.</b> An archived entity still
/// exists, still answers to its id, and everything pointing at it keeps pointing at it - an
/// instance goes on tracking an archived profile, a savegame goes on following one. It is simply
/// not in the lists any more. Anything else would make the archive a second kind of deletion
/// wearing a gentler word.
/// </para>
/// <para>
/// <b>An archived entity does not hold its name.</b> The uniqueness indexes are filtered on
/// <see cref="ArchivedAt"/>, so the name is free the moment it is archived and any number of
/// archived things may share one - they are told apart by when they were archived. The cost lands
/// on the way back: restoring is where a clash has to be resolved, by renaming, which is the right
/// place for it because that is when somebody is present to decide.
/// </para>
/// </remarks>
public interface IArchivable
{
    /// <summary>When this was archived, or null while it is not.</summary>
    DateTime? ArchivedAt { get; }

    bool IsArchived => ArchivedAt is not null;
}
