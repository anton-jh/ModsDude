using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.ComponentModel;
using System.Windows.Data;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

public partial class RepoModsImportPageViewModel
    : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly ModCatalog _catalog;
    private readonly ModListItemViewModel.Factory _itemFactory;
    private readonly CancellationTokenSource _cancellation = new();

    private IReadOnlyList<ModListItemViewModel> _mods = [];
    private bool _suspendSelectionTracking;


    public RepoModsImportPageViewModel(Repo repo, ModCatalog catalog, ModListItemViewModel.Factory itemFactory)
    {
        _repo = repo;
        _catalog = catalog;
        _itemFactory = itemFactory;

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

    /// <summary>True once loading has finished and the sources turned up nothing.</summary>
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
        // per row, then _catalog.Invalidate().
        await Task.CompletedTask;
    }

    private bool CanImport()
        => SelectedCount > 0;


    /// <summary>
    /// Called when the user navigates away. Stops this page waiting on the catalog; the scans
    /// themselves belong to the shell, which cancels them when the whole Mods page goes.
    /// </summary>
    public void Dispose()
    {
        // Deliberately not disposed: the wait may still be inside the token's registration, and
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
        var snapshot = await _catalog.GetAsync(_cancellation.Token);

        // With a single source every row would name the same one, which is just noise.
        var showSources = snapshot.Sources.Count(x => x.IsEnabled) > 1;

        _mods = snapshot.Versions
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(x =>
            {
                var item = _itemFactory.Create(x);

                item.Status = x.GetImportStatus();

                if (showSources && x.FoundIn.Count > 0)
                {
                    item.Instances = string.Join(", ", x.FoundIn.Select(source => source.Source.Name));
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
        public RepoModsImportPageViewModel Create(Repo repo, ModCatalog catalog)
            => ActivatorUtilities.CreateInstance<RepoModsImportPageViewModel>(serviceProvider, repo, catalog);
    }
}
