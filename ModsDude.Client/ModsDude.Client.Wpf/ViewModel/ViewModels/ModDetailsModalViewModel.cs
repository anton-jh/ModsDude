using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.ViewModel.Services;
using System.Collections.ObjectModel;
using System.Windows.Media;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// Everything about a mod that doesn't fit in a list row: the full description, the metadata, and
/// whatever images the mod ships.
/// </summary>
public partial class ModDetailsModalViewModel : ModalViewModel
{
    private readonly IModImageProvider _imageProvider;

    private int _fullImageRequest;


    public ModDetailsModalViewModel(LocalMod mod, IModImageProvider imageProvider)
    {
        _imageProvider = imageProvider;

        Mod = mod;

        // Store images are the presentable ones, but plenty of mods - script mods especially -
        // ship nothing but an icon.
        var images = mod.Images.Count > 0
            ? mod.Images
            : mod.Icon is null ? [] : (IReadOnlyList<LocalModImage>)[mod.Icon];

        Images = new ObservableCollection<ModImageViewModel>(images.Select(x => new ModImageViewModel(x, imageProvider)));
        SelectedImage = Images.FirstOrDefault();
    }


    public LocalMod Mod { get; }

    public string Name => Mod.Name;
    public string Version => Mod.Version;
    public string Id => Mod.Id;
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


    [RelayCommand]
    private void Close()
    {
        Done = true;
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
