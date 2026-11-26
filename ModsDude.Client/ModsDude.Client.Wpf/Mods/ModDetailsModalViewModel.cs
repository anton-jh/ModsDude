using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Imagery;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.Mods.Imaging;
using ModsDude.Client.Wpf.Shell.Modals;
using System.Collections.ObjectModel;
using System.Windows.Media;

namespace ModsDude.Client.Wpf.Mods;

/// <summary>
/// Everything about a mod that doesn't fit in a list row: the full description, the metadata, and
/// whatever images the mod ships.
/// </summary>
public partial class ModDetailsModalViewModel : ModalViewModel
{
    private readonly IModImageProvider _imageProvider;

    private int _fullImageRequest;


    public ModDetailsModalViewModel(CatalogModVersion mod, ModVersionImagery imagery, IModImageProvider imageProvider)
    {
        _imageProvider = imageProvider;

        Mod = mod;

        // Store images are the presentable ones, but plenty of mods - script mods especially -
        // ship nothing but an icon.
        var images = imagery.Images.Count > 0
            ? imagery.Images
            : imagery.Icon is null ? [] : (IReadOnlyList<ModImage>)[imagery.Icon];

        Images = new ObservableCollection<ModImageViewModel>(images.Select(x => new ModImageViewModel(x, imageProvider)));
        SelectedImage = Images.FirstOrDefault();
    }


    public CatalogModVersion Mod { get; }

    public string Name => Mod.Name;
    public string Version => Mod.VersionId.Value;
    public string Id => Mod.ModId.Value;
    public string? Author => Mod.Author;
    public string Description => string.IsNullOrWhiteSpace(Mod.Description)
        ? "This mod doesn't describe itself."
        : Mod.Description;

    public ObservableCollection<ModImageViewModel> Images { get; }

    public bool HasImages => Images.Count > 0;
    public bool HasImageStrip => Images.Count > 1;
    public bool HasAuthor => string.IsNullOrWhiteSpace(Author) is false;

    [ObservableProperty]
    private ModImageViewModel? _selectedImage;

    [ObservableProperty]
    private ImageSource? _fullImage;

    [ObservableProperty]
    private bool _isLoadingFullImage;


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DescriptionSegments), nameof(MatchSummary))]
    [NotifyCanExecuteChangedFor(nameof(FindNextCommand), nameof(FindPreviousCommand))]
    private bool _isFindOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DescriptionSegments), nameof(MatchSummary))]
    [NotifyCanExecuteChangedFor(nameof(FindNextCommand), nameof(FindPreviousCommand))]
    private string _findQuery = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DescriptionSegments), nameof(MatchSummary))]
    private int _currentMatch;

    /// <summary>Goes up each time the find box should take focus.</summary>
    [ObservableProperty]
    private int _findFocusRequests;

    private IReadOnlyList<TextMatch> Matches => IsFindOpen ? TextSearch.FindMatches(Description, FindQuery) : [];

    public IReadOnlyList<TextSegment> DescriptionSegments
    {
        get
        {
            var matches = Matches;

            return TextSearch.Segment(Description, matches, matches.Count > 0 ? CurrentMatch : null);
        }
    }

    public string MatchSummary => Matches.Count switch
    {
        0 when string.IsNullOrWhiteSpace(FindQuery) => "",
        0 => "No matches",
        var count => $"{CurrentMatch + 1} / {count}"
    };


    [RelayCommand]
    private void Close()
    {
        Done = true;
    }

    [RelayCommand]
    private void OpenFind()
    {
        IsFindOpen = true;
        FindFocusRequests++;
    }

    [RelayCommand]
    private void CloseFind()
    {
        IsFindOpen = false;
    }

    [RelayCommand(CanExecute = nameof(HasMatches))]
    private void FindNext()
    {
        CurrentMatch = (CurrentMatch + 1) % Matches.Count;
    }

    [RelayCommand(CanExecute = nameof(HasMatches))]
    private void FindPrevious()
    {
        var count = Matches.Count;

        CurrentMatch = (CurrentMatch - 1 + count) % count;
    }

    private bool HasMatches() => Matches.Count > 0;


    public override bool IsDismissable => true;

    /// <summary>Escape closes the find bar first, then the modal.</summary>
    public override bool TryCancel() => IsFindOpen ? Press(CloseFindCommand) : Press(CloseCommand);

    /// <summary>Enter steps to the next match while finding. Taken even without matches, so a miss doesn't close the modal.</summary>
    public override bool TryAccept()
    {
        if (IsFindOpen)
        {
            Press(FindNextCommand);

            return true;
        }

        return Press(CloseCommand);
    }

    public override void Find() => OpenFind();


    partial void OnFindQueryChanged(string value)
    {
        CurrentMatch = 0;
    }

    partial void OnIsFindOpenChanged(bool value)
    {
        CurrentMatch = 0;
    }

    partial void OnSelectedImageChanged(ModImageViewModel? value)
    {
        _ = LoadFullImage(value);
    }

    private async Task LoadFullImage(ModImageViewModel? image)
    {
        // Clicking through the strip faster than images decode would otherwise let an earlier
        // load land after a later one.
        var request = Interlocked.Increment(ref _fullImageRequest);

        FullImage = null;

        if (image is null)
        {
            return;
        }

        IsLoadingFullImage = true;

        var loaded = await _imageProvider.GetAsync(image.Image, IModImageProvider.FullSize, CancellationToken.None);

        if (request != Volatile.Read(ref _fullImageRequest))
        {
            return;
        }

        FullImage = loaded;
        IsLoadingFullImage = false;
    }
}
