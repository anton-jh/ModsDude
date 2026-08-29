using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Wpf.ViewModel.Pages;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;
public class ProfileItemViewModel(
    Repo repo,
    ProfileDto profile,
    ProfilePageViewModel.Factory profilePageViewModelFactory)
    : MenuItemViewModel(
        profile.Name,
        () => profilePageViewModelFactory.Create(repo, profile))
{
    public Guid Id => profile.Id;


    /// <summary>
    /// Re-reads the DTO, which is updated in place on rename. A generated DTO cannot raise
    /// <see cref="System.ComponentModel.INotifyPropertyChanged"/>, so the entry cannot follow it the
    /// way the repo entries follow their model - and replacing the entry would take the selection
    /// that is on it with it.
    /// </summary>
    public void RefreshTitle()
    {
        Title = profile.Name;
    }
}
