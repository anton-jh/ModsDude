using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Domain.Mods;

public class ModVersion
{
    public required RepoId RepoId { get; init; }
    public required ModId ModId { get; init; }
    public required ModVersionId Id { get; init; }

    public required int SequenceNumber { get; set; }
    public required string DisplayName { get; set; }
    public required string Description { get; set; }

    /// <summary>
    /// SHA-256 of the mod file. A real property rather than a <see cref="ModAttribute"/>: the
    /// system depends on it to address content, so it cannot be something an adapter may omit.
    /// </summary>
    public required string ContentHash { get; set; }

    /// <summary>
    /// The mod itself is version-sensitive, as determined by the adapter from the mod file at
    /// registration. The per-profile override lives on ModDependency.
    /// </summary>
    public required bool Locked { get; set; }

    public required HashSet<ModAttribute> Attributes { get; init; }
    public List<ModImageReference> Images { get; init; } = [];

    public required DateTimeOffset Created { get; init; }
    public required DateTimeOffset Updated { get; set; }
}


public readonly record struct ModVersionId(string Value);
