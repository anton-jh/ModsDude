using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.Profiles;
using ModsDude.Client.Wpf.Shell.Sidebar;

namespace ModsDude.Client.Wpf.Repos;

/// <summary>
/// Follows the repo's name rather than snapshotting it: a rename updates the model in place instead
/// of rebuilding the list, so nothing else would move the entry to its new position.
/// </summary>
public class RepoItemViewModel
    : MenuItemViewModel
{
    private readonly Repo _repo;


    public RepoItemViewModel(
        Repo repo,
        RepoPageViewModel.Factory repoPageViewModelFactory)
        : base(
            repo.Name,
            () => repoPageViewModelFactory.Create(repo),
            repo,
            () => repo.Name,
            nameof(Repo.Name))
    {
        _repo = repo;

        Icon = MenuIcons.Repo;
    }


    public Guid Id => _repo.Id;

    public override bool IsEntity => true;

    /// <summary>The repo's own name, without whatever the sidebar has decided to draw beside it.</summary>
    public string Name => _repo.Name;

    /// <summary>
    /// Which game this repo is for, which is what the sidebar groups by.
    /// </summary>
    /// <remarks>
    /// <b>Not observable, because it cannot change.</b> The adapter's game discriminator is the one
    /// base setting deliberately not marked <c>[CanBeModified]</c> - a repo cannot switch to another
    /// game, since that would orphan every game on every member's machine - so an entry never
    /// moves between groups and there is nothing for the grouping to have to react to.
    /// </remarks>
    public GameHeading Game => new(_repo.Adapter.GameDisplayName, _repo.Adapter.GameShortName);


    /// <summary>
    /// Marks the entry when the profile its game follows is anything but in sync.
    /// </summary>
    /// <remarks>
    /// <b>Only the trouble, never the all-clear.</b> A green mark on every repo whose game is fine
    /// would turn the list into noise; the profile rows inside the repo say "in sync", and this says
    /// "something over there needs you" from wherever the user is standing.
    /// </remarks>
    public void RefreshSyncState(IProfileSyncStatusService syncStatus)
    {
        var state = syncStatus.StateOf(_repo);

        SyncState = state is ProfileSyncState.InSync ? ProfileSyncState.None : state;
    }

    /// <summary>
    /// Shows or hides this entry's tag. Called by whoever owns the list, because whether two entries
    /// read the same is a question about the list and not about either repo.
    /// </summary>
    public void ShowTagIf(bool isAmbiguous)
    {
        Tag = isAmbiguous ? _repo.Tag : null;
    }
}

/// <summary>
/// The heading over one game's repos in the sidebar: its name while the sidebar is open, and its short
/// name in a rail, where the name does not fit.
/// </summary>
/// <remarks>
/// A record because it is the grouping key itself - the view groups by equality, so two repos for the
/// same game build two equal headings and land in one group, as they did when this was a bare name.
/// </remarks>
public sealed record GameHeading(string Name, string ShortName);
