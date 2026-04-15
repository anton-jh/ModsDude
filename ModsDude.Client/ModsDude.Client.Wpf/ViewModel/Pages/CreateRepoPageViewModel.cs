using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;
public partial class CreateRepoPageViewModel(
    RepoRepository repoRepository,
    IGameAdapterIndex gameAdapterIndex,
    NavigationLockService navigationLockService,
    IDialogService dialogService,
    IModalService modalService)
    : PageViewModel, IDisposable
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BaseSettingsEditor))]
    private IGameAdapter? _selectedGameAdapter;


    public DynamicFormViewModel? BaseSettingsEditor => SelectedGameAdapter?.GetBaseSettingsTemplate() is DynamicForm template
        ? new(editing: false, template, dialogService)
        : null;

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Name) &&
        SelectedGameAdapter is not null &&
        (BaseSettingsEditor?.IsValid ?? false);

    public ObservableCollection<IGameAdapter> AvailableGameAdapters { get; } =
        new(gameAdapterIndex.GetAllLatest());


    [RelayCommand]
    public async Task Submit(CancellationToken cancellationToken)
    {
        if (!IsValid || SelectedGameAdapter is null || string.IsNullOrWhiteSpace(Name) || BaseSettingsEditor is null)
        {
            var modal = ConfirmationDialogViewModel.ValidationErrors(GetValidationErrors());
            await modalService.Show(modal);

            return;
        }

        navigationLockService.ReleaseLock(this);

        await repoRepository.CreateRepo(
            Name,
            SelectedGameAdapter.Id.ToString(),
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
        if (SelectedGameAdapter is null)
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

    partial void OnSelectedGameAdapterChanged(IGameAdapter? value)
    {
        navigationLockService.AcquireLock(this);
    }
}
