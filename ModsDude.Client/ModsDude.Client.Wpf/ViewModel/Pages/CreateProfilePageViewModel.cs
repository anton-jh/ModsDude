using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Services;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

public partial class CreateProfilePageViewModel(
    Repo repo,
    ProfileService profileService,
    NavigationLockService navigationLockService)
    : PageViewModel, IDisposable
{
    private readonly Repo _repo = repo;


    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    private string _name = "";

    public string RepoName => _repo.Name;

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


    public class Factory(IServiceProvider serviceProvider)
    {
        public CreateProfilePageViewModel Create(Repo repo)
            => ActivatorUtilities.CreateInstance<CreateProfilePageViewModel>(serviceProvider, repo);
    }
}
