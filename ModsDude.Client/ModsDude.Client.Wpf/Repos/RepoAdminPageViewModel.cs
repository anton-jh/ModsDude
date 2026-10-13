using ModsDude.Client.Core.Repos;

﻿using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Games;
using ModsDude.Client.Wpf.Shared;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Navigation;
using ModsDude.Client.Wpf.Shell.Toasts;
using System.ComponentModel;

namespace ModsDude.Client.Wpf.Repos;
public partial class RepoAdminPageViewModel : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly IRepoStore _repoStore;
    private readonly INavigationLockService _navigationLockService;
    private readonly IModalService _modalService;
    private readonly IToastService _toasts;


    public RepoAdminPageViewModel(
        Repo repo,
        IRepoStore repoStore,
        INavigationLockService navigationLockService,
        IModalService modalService,
        IToastService toasts,
        IFilePickerService filePickerService)
    {
        _repo = repo;
        _repoStore = repoStore;
        _navigationLockService = navigationLockService;
        _modalService = modalService;
        _toasts = toasts;
        _name = repo.Name;
        BaseSettingsEditor = new(true, repo.Adapter.BaseSettings.Copy(), filePickerService);

        BaseSettingsEditor.PropertyChanged += OnBaseSettingsChanged;
    }


    [ObservableProperty]
    private string _name;

    /// <summary>The name as the server has it. Read from the model rather than snapshotted, because saving now updates the model in place instead of rebuilding it.</summary>
    public string OriginalName => _repo.Name;

    public DynamicFormViewModel BaseSettingsEditor { get; }

    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && BaseSettingsEditor.IsValid;

    public bool HasChanges => Name != OriginalName || BaseSettingsEditor.HasChanges;


    [RelayCommand(CanExecute = nameof(HasChanges))]
    public async Task SaveChanges(CancellationToken cancellationToken)
    {
        if (!IsValid)
        {
            var modal = ConfirmationModalViewModel.ValidationErrors(GetValidationErrors());
            await _modalService.Show(modal);
            return;
        }

        await _repo.Update(Name, BaseSettingsEditor.ExtractResults(), cancellationToken);

        OnPropertyChanged(nameof(OriginalName));
        BaseSettingsEditor.MarkSaved();
        OnHasChangesChanged();

        _toasts.Show($"Saved the settings for {OriginalName}.");
    }

    /// <summary>
    /// Puts the repo in the top-level Archive rather than deleting it - for every member at once,
    /// since archiving is repo state and not membership state.
    /// </summary>
    [RelayCommand]
    public async Task ArchiveRepo(CancellationToken cancellationToken)
    {
        if (await ConfirmArchive())
        {
            _navigationLockService.ReleaseLock(this);
            await _repoStore.ArchiveRepo(_repo.Id, cancellationToken);
        }
    }

    public void Dispose()
    {
        _navigationLockService.ReleaseLock(this);
        BaseSettingsEditor.PropertyChanged -= OnBaseSettingsChanged;
        BaseSettingsEditor.Dispose();
    }


    partial void OnNameChanged(string value)
    {
        OnHasChangesChanged();
    }

    private void OnHasChangesChanged()
    {
        OnPropertyChanged(nameof(HasChanges));
        SaveChangesCommand.NotifyCanExecuteChanged();
        _navigationLockService.SetLock(this, HasChanges);
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

    private async Task<bool> ConfirmArchive()
    {
        var modal = ConfirmationModalViewModel.ConfirmArchive(OriginalName, "repo");

        await _modalService.Show(modal);

        return modal.Result;
    }

    private void OnBaseSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DynamicFormViewModel.HasChanges))
        {
            OnHasChangesChanged();
        }
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public RepoAdminPageViewModel Create(Repo repo)
            => ActivatorUtilities.CreateInstance<RepoAdminPageViewModel>(serviceProvider, repo);
    }
}
