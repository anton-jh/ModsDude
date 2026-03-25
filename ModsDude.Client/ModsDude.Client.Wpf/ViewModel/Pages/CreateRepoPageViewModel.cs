using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Services;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;
public partial class CreateRepoPageViewModel(
    RepoService repoService,
    IGameAdapterIndex gameAdapterIndex,
    NavigationLockService navigationLockService)
    : PageViewModel, IDisposable
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    private string _name = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    [NotifyPropertyChangedFor(nameof(SelectedGameAdapter))]
    [NotifyPropertyChangedFor(nameof(AdapterConfigurationModel))]
    private GameAdapterDescriptor? _selectedGameAdapterDescriptor;

    
    public IGameAdapter? SelectedGameAdapter => SelectedGameAdapterDescriptor is not null
        ? gameAdapterIndex.GetById(SelectedGameAdapterDescriptor.Value.Id)
        : null;

    public object? AdapterConfigurationModel => SelectedGameAdapter?.BaseSettingsTemplate; // TODO: probably replace with DynamicFormViewModel

    public bool IsValid =>
        !string.IsNullOrEmpty(Name) &&
        SelectedGameAdapterDescriptor is not null;

    public ObservableCollection<GameAdapterDescriptor> AvailableGameAdapters { get; } =
        [.. gameAdapterIndex.GetAllLatest().Select(x => x.Descriptor)];


    [RelayCommand(CanExecute = nameof(IsValid))]
    public async Task Submit(CancellationToken cancellationToken)
    {
        if (SelectedGameAdapterDescriptor is null || string.IsNullOrWhiteSpace(Name))
        {
            return;
        }

        navigationLockService.ReleaseLock(this);

        await repoService.CreateRepo(
            Name,
            SelectedGameAdapterDescriptor.Value.Id.ToString(),
            "",
            cancellationToken);
    }

    public void Dispose()
    {
        navigationLockService.ReleaseLock(this);
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
