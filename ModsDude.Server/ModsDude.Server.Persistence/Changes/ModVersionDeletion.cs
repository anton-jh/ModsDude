using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Persistence.Changes;

/// <summary>
/// A mod version that was deleted, at the change number its deletion took. Written only by the
/// database's delete trigger, so no way of deleting a version can forget it.
/// </summary>
public class ModVersionDeletion
{
    public required RepoId RepoId { get; init; }
    public required ModId ModId { get; init; }
    public required ModVersionId VersionId { get; init; }
    public required long ChangeSequence { get; init; }
}
