using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.ViewModel.Pages;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

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

    /// <summary>The repo's own name, without whatever the sidebar has decided to draw beside it.</summary>
    public string Name => _repo.Name;

    /// <summary>
    /// Which game this repo is for, which is what the sidebar groups by.
    /// </summary>
    /// <remarks>
    /// <b>Not observable, because it cannot change.</b> The adapter's game discriminator is the one
    /// base setting deliberately not marked <c>[CanBeModified]</c> - an FS22 repo cannot become an
    /// FS25 one, since that would orphan every instance on every member's machine - so an entry never
    /// moves between groups and there is nothing for the grouping to have to react to.
    /// </remarks>
    public string GameName => _repo.Adapter.GameDisplayName;


    /// <summary>
    /// Shows or hides this entry's tag. Called by whoever owns the list, because whether two entries
    /// read the same is a question about the list and not about either repo.
    /// </summary>
    public void ShowTagIf(bool isAmbiguous)
    {
        Tag = isAmbiguous ? _repo.Tag : null;
    }
}
