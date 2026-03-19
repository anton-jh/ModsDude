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
    : PageViewModel, IDisposable
{
    [ObservableProperty]
    private string _name = repo.Name;

    public string OriginalName { get; } = repo.Name;


    [RelayCommand]
    public async Task SaveChanges(CancellationToken cancellationToken)
    {
        navigationLockService.ReleaseLock(this);
        await repoService.UpdateRepo(repo.Id, Name, cancellationToken);
    }

    [RelayCommand]
    public async Task DeleteRepo(CancellationToken cancellationToken)
    {
        navigationLockService.ReleaseLock(this);
        await repoService.DeleteRepo(repo.Id, cancellationToken);
    }

    public void Dispose()
    {
        navigationLockService.ReleaseLock(this);
    }


    partial void OnNameChanged(string value)
    {
        navigationLockService.AcquireLock(this);
    }
}
