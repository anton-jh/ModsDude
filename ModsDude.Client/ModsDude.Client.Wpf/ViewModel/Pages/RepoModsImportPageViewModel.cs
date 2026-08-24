using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.ComponentModel;
using System.Windows.Data;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

public partial class RepoModsImportPageViewModel
    : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly IBaseModAdapter _baseModAdapter;
    private readonly ModListItemViewModel.Factory _itemFactory;
    private readonly CancellationTokenSource _cancellation = new();

    /// <summary>How long the page must survive before it is worth scanning the mod folders.</summary>
    private static readonly TimeSpan _scanDelay = TimeSpan.FromMilliseconds(150);

    private IReadOnlyList<ModListItemViewModel> _mods = [];
    private bool _suspendSelectionTracking;


    public RepoModsImportPageViewModel(Repo repo, ModListItemViewModel.Factory itemFactory)
    {
        _repo = repo;
        _itemFactory = itemFactory;
        _baseModAdapter = repo.Adapter.GetBaseCapabilityAdapterFactory<IBaseModAdapter>()?.Invoke()
            ?? throw UserFriendlyException.RepoNoModSupport();

        RepoName = repo.Name;
    }


    public string RepoName { get; }

    [ObservableProperty]
    private ICollectionView? _modsView;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportButtonText))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private int _selectedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMods))]
    [NotifyPropertyChangedFor(nameof(CountText))]
    private int _totalCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    [NotifyPropertyChangedFor(nameof(HasVisibleMods))]
    private int _visibleCount;

    /// <summary>True once loading has finished and the instances turned up nothing.</summary>
    [ObservableProperty]
    private bool _isEmpty;


    public bool HasMods => TotalCount > 0;
    public bool HasVisibleMods => VisibleCount > 0;
    public bool HasInstances => _repo.LocalInstances.Count > 0;

    public string CountText => VisibleCount == TotalCount
        ? $"{TotalCount} mods"
        : $"{VisibleCount} of {TotalCount} mods";

    public string ImportButtonText => SelectedCount switch
    {
        0 => "Import selected",
        1 => "Import 1 mod",
        _ => $"Import {SelectedCount} mods"
    };


    [RelayCommand]
    public void SelectAll()
        => SetSelectionOfVisible(true);

    [RelayCommand]
    public void SelectNone()
        => SetSelectionOfVisible(false);

    [RelayCommand]
    public void ClearSearch()
        => SearchText = string.Empty;

    [RelayCommand(CanExecute = nameof(CanImport))]
    public async Task ImportAsync()
    {
        var selected = _mods.Where(x => x.IsSelected).Select(x => x.Mod).ToList();

        // TODO: upload each mod through the server's mod upload link endpoint, reporting progress
        // per row.
        await Task.CompletedTask;
    }

    private bool CanImport()
        => SelectedCount > 0;


    /// <summary>
    /// Called when the user navigates away. Scanning a mod folder is the most expensive thing this
    /// app does, and there is no point finishing it for a page nobody is looking at.
    /// </summary>
    public void Dispose()
    {
        // Deliberately not disposed: the scan may still be inside the token's registration, and
        // disposing a source out from under that is not safe. Nothing here holds a wait handle,
        // so letting it be collected costs nothing.
        _cancellation.Cancel();
    }

    /// <summary>
    /// A cancelled scan is the expected outcome of navigating away, not something to show the user
    /// an error modal about.
    /// </summary>
    protected override void OnInitFailed(Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            return;
        }

        base.OnInitFailed(ex);
    }


    protected override async Task InitAsync()
    {
        // Dragging across the menu builds and discards one page per item it passes over. Holding
        // off briefly means a page nobody actually stopped on never touches the disk at all.
        await Task.Delay(_scanDelay, _cancellation.Token);

        // Which instances a mod was found in - the same mod is usually installed in several.
        var sources = new Dictionary<(string Id, string Version), List<string>>();
        var mods = new List<LocalMod>();

        foreach (var instance in _repo.LocalInstances)
        {
            var installedMods = await _baseModAdapter
                .WithInstanceSettings(instance.InstanceSettings)
                .GetInstalledMods(_cancellation.Token);

            foreach (var mod in installedMods)
            {
                mods.Add(mod);

                if (sources.TryGetValue((mod.Id, mod.Version), out var instances))
                {
                    instances.Add(instance.Name);
                }
                else
                {
                    sources[(mod.Id, mod.Version)] = [instance.Name];
                }
            }
        }

        // With a single instance every row would name the same one, which is just noise.
        var showInstances = _repo.LocalInstances.Count > 1;

        _mods = mods
            .DistinctBy(x => (x.Id, x.Version))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(x =>
            {
                var item = _itemFactory.Create(x);

                if (showInstances)
                {
                    item.Instances = string.Join(", ", sources[(x.Id, x.Version)]);
                }

                item.PropertyChanged += OnModPropertyChanged;
                return item;
            })
            .ToList();
    }

    protected override void OnInitCompleted()
    {
        // The view has to be created on the UI thread, and the list is only complete now.
        var view = CollectionViewSource.GetDefaultView(_mods);
        view.Filter = x => x is ModListItemViewModel mod && mod.Matches(SearchText);

        ModsView = view;
        TotalCount = _mods.Count;
        // Respects anything typed into the search box while the scan was still running.
        VisibleCount = _mods.Count(x => x.Matches(SearchText));
        IsEmpty = _mods.Count == 0;
        IsLoading = false;
    }


    partial void OnSearchTextChanged(string value)
    {
        ModsView?.Refresh();
        VisibleCount = _mods.Count(x => x.Matches(value));
    }

    private void OnModPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(ModListItemViewModel.IsSelected) || _suspendSelectionTracking)
        {
            return;
        }

        RecountSelection();
    }

    private void SetSelectionOfVisible(bool isSelected)
    {
        _suspendSelectionTracking = true;

        try
        {
            foreach (var mod in _mods.Where(x => x.Matches(SearchText)))
            {
                mod.IsSelected = isSelected;
            }
        }
        finally
        {
            _suspendSelectionTracking = false;
        }

        RecountSelection();
    }

    private void RecountSelection()
        => SelectedCount = _mods.Count(x => x.IsSelected);


    public class Factory(IServiceProvider serviceProvider)
    {
        public RepoModsImportPageViewModel Create(Repo repo)
            => ActivatorUtilities.CreateInstance<RepoModsImportPageViewModel>(serviceProvider, repo);
    }
}
