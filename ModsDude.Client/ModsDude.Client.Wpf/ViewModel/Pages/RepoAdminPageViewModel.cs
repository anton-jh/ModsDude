using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;

namespace ModsDude.Client.Wpf.ViewModel.Pages;
public partial class RepoAdminPageViewModel : PageViewModel, IDisposable
{
    private readonly RepoModel _repo;
    private readonly RepoService _repoService;
    private readonly NavigationLockService _navigationLockService;
    private readonly IModalService _modalService;


    public RepoAdminPageViewModel(
        RepoModel repo,
        RepoService repoService,
        NavigationLockService navigationLockService,
        IModalService modalService,
        IDialogService dialogService)
    {
        _repo = repo;
        _repoService = repoService;
        _navigationLockService = navigationLockService;
        _modalService = modalService;
        _name = repo.Name;
        OriginalName = repo.Name;
        BaseSettingsEditor = new(true, repo.AdapterConfiguration, dialogService);

        BaseSettingsEditor.Modified += OnBaseSettingsModified;
    }


    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
    private string _name;

    public string OriginalName { get; }

    public DynamicFormViewModel BaseSettingsEditor { get; }

    public bool IsValid => !string.IsNullOrWhiteSpace(Name);


    [RelayCommand(CanExecute = nameof(IsValid))]
    public async Task SaveChanges(CancellationToken cancellationToken)
    {
        _navigationLockService.ReleaseLock(this);
        await _repoService.UpdateRepo(_repo.Id, Name, BaseSettingsEditor.ExtractResults(), cancellationToken);
    }

    [RelayCommand]
    public async Task DeleteRepo(CancellationToken cancellationToken)
    {
        if (await ConfirmDelete())
        {
            _navigationLockService.ReleaseLock(this);
            await _repoService.DeleteRepo(_repo.Id, cancellationToken);
        }
    }

    public void Dispose()
    {
        _navigationLockService.ReleaseLock(this);
        BaseSettingsEditor.Modified -= OnBaseSettingsModified;
        BaseSettingsEditor.Dispose();
    }


    partial void OnNameChanged(string value)
    {
        _navigationLockService.AcquireLock(this);
    }

    private async Task<bool> ConfirmDelete()
    {
        var modal = ConfirmationDialogViewModel.ConfirmDelete(OriginalName);

        await _modalService.Show(modal);

        return modal.Result;
    }

    private void OnBaseSettingsModified(object? sender, EventArgs e)
    {
        _navigationLockService.AcquireLock(this);
    }
}
