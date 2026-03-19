using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Navigation;

namespace ModsDude.Client.Wpf.ViewModel.Pages;
public partial class RepoAdminPageViewModel(
    RepoModel repo,
    RepoService repoService,
    NavigationLockService navigationLockService)
    : PageViewModel
{
    [ObservableProperty]
    private string _name = repo.Name;

    public string OriginalName { get; } = repo.Name;


    [RelayCommand]
    private async Task SaveChanges(CancellationToken cancellationToken)
    {
        await repoService.UpdateRepo(repo.Id, Name, cancellationToken);
        navigationLockService.ReleaseLock(this);
    }

    [RelayCommand]
    private Task DeleteRepo(CancellationToken cancellationToken)
    {
        return repoService.DeleteRepo(repo.Id, cancellationToken);
    }

    partial void OnNameChanged(string value)
    {
        navigationLockService.AcquireLock(this);
    }
}
