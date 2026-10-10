using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Wpf.Repos;

namespace ModsDude.Client.Wpf.Shell.Navigation;

/// <summary>A place inside one repo that a link from outside its own pages can open.</summary>
public abstract record RepoDestination
{
    private RepoDestination()
    {
    }


    public sealed record Section(RepoSection Kind) : RepoDestination;

    public sealed record ConnectGame : RepoDestination;

    /// <summary>The list the savegame is in: Saves for a live one, the Archive for an archived one.</summary>
    public sealed record Savegame(Guid SavegameId) : RepoDestination;

    public sealed record Profile(Guid ProfileId) : RepoDestination;

    /// <param name="ScanTarget">The folder to open with already scanned, or null for none.</param>
    public sealed record ProfileMods(Guid ProfileId, ModTargetRef? ScanTarget) : RepoDestination;

    /// <param name="Revision">The revision to open at, or null for the latest.</param>
    public sealed record ProfileHistory(Guid ProfileId, int? Revision) : RepoDestination;
}
