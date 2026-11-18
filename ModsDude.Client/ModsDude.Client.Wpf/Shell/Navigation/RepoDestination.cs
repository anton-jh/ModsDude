using ModsDude.Client.Core.Profiles.Editor;
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

    /// <param name="Review">
    /// Opens the editor to review the game's mod folders: every one of them is the only source, and
    /// the pinned list shows this filter. Null opens it as it is.
    /// </param>
    public sealed record ProfileMods(Guid ProfileId, PinnedModFilter? Review) : RepoDestination;

    /// <param name="Revision">The revision to open at, or null for the latest.</param>
    /// <param name="CompareWith">
    /// Opens the revision's changes compared with this revision. Null opens its contents.
    /// </param>
    public sealed record ProfileHistory(Guid ProfileId, int? Revision, int? CompareWith) : RepoDestination;
}
