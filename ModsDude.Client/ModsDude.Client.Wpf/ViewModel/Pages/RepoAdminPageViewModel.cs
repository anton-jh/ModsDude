using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;

namespace ModsDude.Client.Wpf.ViewModel.Pages;
public partial class RepoAdminPageViewModel : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly RepoRepository _repoService;
    private readonly NavigationLockService _navigationLockService;
    private readonly IModalService _modalService;


    public RepoAdminPageViewModel(
        Repo repo,
        RepoRepository repoService,
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
        BaseSettingsEditor = new(true, repo.Adapter.BaseSettings.Copy(), dialogService);

        BaseSettingsEditor.Modified += OnBaseSettingsModified;
    }


    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
    private string _name;

    public string OriginalName { get; }

    public DynamicFormViewModel BaseSettingsEditor { get; }

    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && BaseSettingsEditor.IsValid;


    [RelayCommand]
    public async Task SaveChanges(CancellationToken cancellationToken)
    {
        if (!IsValid)
        {
            var modal = ConfirmationDialogViewModel.ValidationErrors(GetValidationErrors());
            await _modalService.Show(modal);
            return;
        }

        _navigationLockService.ReleaseLock(this);

        await _repo.Update(Name, BaseSettingsEditor.ExtractResults(), cancellationToken);
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
        _navigationLockService.Dispose();
        BaseSettingsEditor.Modified -= OnBaseSettingsModified;
        BaseSettingsEditor.Dispose();
    }


    partial void OnNameChanged(string value)
    {
        _navigationLockService.AcquireLock(this);
    }

    private List<string> GetValidationErrors()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("Name is required.");
        }
        errors.AddRange(BaseSettingsEditor.GetValidationErrors());

        return errors;
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


    public class Factory(IServiceProvider serviceProvider)
    {
        public RepoAdminPageViewModel Create(Repo repo)
            => ActivatorUtilities.CreateInstance<RepoAdminPageViewModel>(serviceProvider, repo);
    }
}
