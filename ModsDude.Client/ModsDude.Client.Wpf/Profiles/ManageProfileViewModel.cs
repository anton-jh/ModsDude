using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Navigation;

namespace ModsDude.Client.Wpf.Profiles;

/// <summary>Renaming and archiving a profile, as a card on its Overview.</summary>
public partial class ManageProfileViewModel(
    ProfileDto profile,
    IProfileService profileService,
    INavigationLockService navigationLockService,
    IModalService modalService)
    : ObservableObject, IDisposable
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
    private string _name = profile.Name;

    public bool IsValid => !string.IsNullOrWhiteSpace(Name);


    [RelayCommand(CanExecute = nameof(IsValid))]
    private async Task SaveChanges(CancellationToken cancellationToken)
    {
        navigationLockService.ReleaseLock(this);
        await profileService.UpdateProfile(profile.RepoId, profile.Id, Name, cancellationToken);
    }

    /// <summary>
    /// Puts the profile in the repo's Archive rather than deleting it. A profile carries a history
    /// that makes every one of its revisions reproducible, and that is not something to lose from a
    /// card somebody used to rename it.
    /// </summary>
    [RelayCommand]
    private async Task ArchiveProfile(CancellationToken cancellationToken)
    {
        var modal = ConfirmationModalViewModel.ConfirmArchive(profile.Name, "profile");

        await modalService.Show(modal);

        if (modal.Result)
        {
            navigationLockService.ReleaseLock(this);
            await profileService.ArchiveProfile(profile.RepoId, profile.Id, cancellationToken);
        }
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
        public ManageProfileViewModel Create(ProfileDto profile)
            => ActivatorUtilities.CreateInstance<ManageProfileViewModel>(serviceProvider, profile);
    }
}
