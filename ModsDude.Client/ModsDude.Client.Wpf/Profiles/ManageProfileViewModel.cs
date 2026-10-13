using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Navigation;
using ModsDude.Client.Wpf.Shell.Toasts;

namespace ModsDude.Client.Wpf.Profiles;

/// <summary>Renaming and archiving a profile, as a card on its Overview.</summary>
public partial class ManageProfileViewModel(
    Profile profile,
    IProfileStore profileStore,
    INavigationLockService navigationLockService,
    IModalService modalService,
    IToastService toasts)
    : ObservableObject, IDisposable
{
    [ObservableProperty]
    private string _name = profile.Name;

    public bool HasChanges => Name != profile.Name;

    public bool CanSave => HasChanges && string.IsNullOrWhiteSpace(Name) is false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool HasError => Error is not null;


    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveChanges(CancellationToken cancellationToken)
    {
        Error = null;

        try
        {
            await profileStore.RenameAsync(profile, Name, cancellationToken);
        }
        catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type is ProblemType.NameTaken)
        {
            Error = "That name is taken.";

            return;
        }

        OnHasChangesChanged();

        toasts.Show($"Renamed the profile to {profile.Name}.");
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
            await profileStore.ArchiveAsync(profile, cancellationToken);
        }
    }

    public void Dispose()
    {
        navigationLockService.ReleaseLock(this);
    }


    partial void OnNameChanged(string value)
    {
        OnHasChangesChanged();
    }

    private void OnHasChangesChanged()
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(CanSave));
        SaveChangesCommand.NotifyCanExecuteChanged();
        navigationLockService.SetLock(this, HasChanges);
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public ManageProfileViewModel Create(Profile profile)
            => ActivatorUtilities.CreateInstance<ManageProfileViewModel>(serviceProvider, profile);
    }
}
