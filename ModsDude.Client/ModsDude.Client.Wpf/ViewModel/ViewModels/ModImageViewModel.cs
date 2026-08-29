using CommunityToolkit.Mvvm.ComponentModel;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.ViewModel.Services;
using System.Windows.Media;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One entry in the image strip of the mod details dialog. A pack can ship a few dozen of these,
/// so the strip only decodes the ones that scroll into view.
/// </summary>
public partial class ModImageViewModel(ModImage image, IModImageProvider imageProvider)
    : ObservableObject, ILazyLoadable
{
    private bool _requested;


    public ModImage Image { get; } = image;

    public string Name => Image.Name;

    [ObservableProperty]
    private ImageSource? _thumbnail;


    public async Task LoadAsync()
    {
        if (_requested)
        {
            return;
        }

        _requested = true;

        Thumbnail = await imageProvider.GetAsync(Image, IModImageProvider.PreviewSize, CancellationToken.None);
    }
}
