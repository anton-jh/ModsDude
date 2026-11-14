using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Wpf.Repos;

public enum RepoDepartureKind
{
    Leave,
    Archive
}


/// <summary>
/// Leaving or archiving a repo from this machine: refused while anything works for it, and asked
/// about first, naming the savegames still checked out here, with the option to check them in.
/// </summary>
public interface IRepoDeparture
{
    /// <param name="depart">The server call. Runs while everything that works for the repo is claimed.</param>
    /// <returns>Whether <paramref name="depart"/> ran.</returns>
    Task<bool> RunAsync(Repo repo, RepoDepartureKind kind, Func<CancellationToken, Task> depart, CancellationToken cancellationToken);
}
