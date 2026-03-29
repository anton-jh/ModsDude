using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;
public partial class CreateRepoPageViewModel(
    RepoService repoService,
    IGameAdapterIndex gameAdapterIndex,
    NavigationLockService navigationLockService,
    IDialogService dialogService,
    IModalService modalService)
    : PageViewModel, IDisposable
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedGameAdapter))]
    [NotifyPropertyChangedFor(nameof(BaseSettingsEditor))]
    private GameAdapterDescriptor? _selectedGameAdapterDescriptor;

    
    public IGameAdapter? SelectedGameAdapter => SelectedGameAdapterDescriptor is not null
        ? gameAdapterIndex.GetById(SelectedGameAdapterDescriptor.Value.Id)
        : null;

    public DynamicFormViewModel? BaseSettingsEditor => SelectedGameAdapter?.BaseSettingsTemplate is not null
        ? new(false, SelectedGameAdapter.BaseSettingsTemplate, dialogService)
        : null;

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Name) &&
        SelectedGameAdapterDescriptor is not null &&
        (BaseSettingsEditor?.IsValid ?? false);

    public ObservableCollection<GameAdapterDescriptor> AvailableGameAdapters { get; } =
        [.. gameAdapterIndex.GetAllLatest().Select(x => x.Descriptor)];


    [RelayCommand]
    public async Task Submit(CancellationToken cancellationToken)
    {
        if (!IsValid || SelectedGameAdapterDescriptor is null || string.IsNullOrWhiteSpace(Name) || BaseSettingsEditor is null)
        {
            var modal = ConfirmationDialogViewModel.ValidationErrors(GetValidationErrors());
            await modalService.Show(modal);

            return;
        }

        navigationLockService.ReleaseLock(this);

        await repoService.CreateRepo(
            Name,
            SelectedGameAdapterDescriptor.Value.Id.ToString(),
            BaseSettingsEditor,
            cancellationToken);
    }

    public void Dispose()
    {
        navigationLockService.ReleaseLock(this);
    }


    private List<string> GetValidationErrors()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("Name is required.");
        }
        if (SelectedGameAdapterDescriptor is null)
        {
            errors.Add("Game adapter is required.");
        }
        errors.AddRange(BaseSettingsEditor?.GetValidationErrors() ?? []);

        return errors;
    }


    partial void OnNameChanged(string value)
    {
        navigationLockService.AcquireLock(this);
    }

    partial void OnSelectedGameAdapterDescriptorChanged(GameAdapterDescriptor? value)
    {
        navigationLockService.AcquireLock(this);
    }
}
