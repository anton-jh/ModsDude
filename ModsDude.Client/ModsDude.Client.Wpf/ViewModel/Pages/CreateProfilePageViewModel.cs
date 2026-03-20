using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Services;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

public partial class CreateProfilePageViewModel(
    RepoModel repo,
    ProfileService profileService,
    NavigationLockService navigationLockService)
    : PageViewModel, IDisposable
{
    private readonly RepoModel _repo = repo;


    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    private string _name = "";

    public bool IsValid => !string.IsNullOrWhiteSpace(Name);

    [RelayCommand(CanExecute = nameof(IsValid))]
    public async Task Submit(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return;
        }

        navigationLockService.ReleaseLock(this);

        await profileService.CreateProfile(_repo.Id, Name, cancellationToken);
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
