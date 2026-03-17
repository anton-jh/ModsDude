using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Services;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;
public partial class CreateRepoPageViewModel(
    RepoService repoService,
    IGameAdapterIndex gameAdapterIndex)
    : PageViewModel
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

    public object? AdapterConfigurationModel => SelectedGameAdapter?.GetBaseSettingsTemplate();

    public bool IsValid =>
        !string.IsNullOrEmpty(Name) &&
        SelectedGameAdapterDescriptor is not null;

    public ObservableCollection<GameAdapterDescriptor> AvailableGameAdapters { get; } =
        [.. gameAdapterIndex.GetAllLatest().Select(x => x.Descriptor)];


    [RelayCommand(CanExecute = nameof(IsValid))]
    private async Task Submit(CancellationToken cancellationToken)
    {
        if (SelectedGameAdapterDescriptor is null || string.IsNullOrWhiteSpace(Name))
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
