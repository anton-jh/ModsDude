using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Persistence.Changes;

/// <summary>
/// How many changes a repo's mods have had, counted by the database itself.
/// </summary>
/// <remarks>
/// Written only by the triggers the <c>ModChangeSequences</c> migration installs, never by code. Every
/// insert, update and delete of a mod version takes the next number while holding this row's lock
/// until it commits, so the numbers a reader can see are always a gapless, committed prefix - which a
/// timestamp, or a database sequence, cannot promise.
/// </remarks>
public class RepoChangeCounter
{
    public required RepoId RepoId { get; init; }
    public long Mods { get; init; }
}
