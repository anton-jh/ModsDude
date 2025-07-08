using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.WindowsClient.Model.GameAdapters;
using ModsDude.WindowsClient.Model.Services;
using System.Collections.ObjectModel;

namespace ModsDude.WindowsClient.ViewModel.Pages;
public partial class CreateRepoPageViewModel(
    RepoService repoService,
    GameAdapterRegistry gameAdapterRegistry)
    : PageViewModel
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    private string _name = "New repo";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    [NotifyPropertyChangedFor(nameof(SelectedGameAdapter))]
    [NotifyPropertyChangedFor(nameof(AdapterConfigurationModel))]
    private GameAdapterDescriptor? _selectedGameAdapterDescriptor;


    public IGameAdapter? SelectedGameAdapter => SelectedGameAdapterDescriptor is not null
        ? gameAdapterRegistry.Get(SelectedGameAdapterDescriptor.Value.Id)
        : null;

    public object? AdapterConfigurationModel => SelectedGameAdapter?.GetBaseConfigurationTemplate();

    public bool IsValid =>
        !string.IsNullOrEmpty(Name) &&
        SelectedGameAdapterDescriptor is not null;

    public ObservableCollection<GameAdapterDescriptor> AvailableGameAdapters { get; } = [.. gameAdapterRegistry.Descriptors];


    [RelayCommand(CanExecute = nameof(IsValid))]
    private async Task Submit(CancellationToken cancellationToken)
    {
        if (SelectedGameAdapterDescriptor is null)
        {
            return;
        }

        await repoService.CreateRepo(
            Name,
            SelectedGameAdapterDescriptor.Value.Id.ToString(),
            "",
            cancellationToken);
    }
}
