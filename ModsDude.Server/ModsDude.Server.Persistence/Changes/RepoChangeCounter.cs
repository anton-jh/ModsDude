using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Persistence.Changes;

/// <summary>
/// How many changes each part of a repo has had, counted by the database itself.
/// </summary>
/// <remarks>
/// Written only by the triggers the <c>ModChangeSequences</c> and <c>RepoChangeCounters</c>
/// migrations install, never by code, so no way of changing a row can forget to count it. A write
/// takes this row's lock until it commits, so a count a reader sees only covers committed changes.
/// <see cref="Mods"/> is also the gapless sequence the mod feed is numbered by.
/// </remarks>
public class RepoChangeCounter
{
    public required RepoId RepoId { get; init; }

    /// <summary>The repo's own row: name, settings, archiving.</summary>
    public long Repo { get; init; }

    public long Profiles { get; init; }

    /// <summary>Savegames, their snapshots and their claims.</summary>
    public long Savegames { get; init; }

    public long Mods { get; init; }

    public long Members { get; init; }

    /// <summary>Which profile each member's games are on.</summary>
    public long Activity { get; init; }
}
