using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.ViewModel.Pages;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// Follows the repo's name rather than snapshotting it: a rename updates the model in place instead
/// of rebuilding the list, so nothing else would move the entry to its new position.
/// </summary>
public class RepoItemViewModel(
    Repo repo,
    RepoPageViewModel.Factory repoPageViewModelFactory)
    : MenuItemViewModel(
        repo.Name,
        () => repoPageViewModelFactory.Create(repo),
        repo,
        () => repo.Name,
        nameof(Repo.Name))
{
    public Guid Id => repo.Id;

    /// <summary>The repo's own name, without whatever the sidebar has decided to draw beside it.</summary>
    public string Name => repo.Name;

    /// <summary>
    /// Shows or hides this entry's tag. Called by whoever owns the list, because whether two entries
    /// read the same is a question about the list and not about either repo.
    /// </summary>
    public void ShowTagIf(bool isAmbiguous)
    {
        Tag = isAmbiguous ? repo.Tag : null;
    }
}
