using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// How one person is drawn in a circle: their picture where they have one, and their initial on
/// their tag's colour until it arrives or where they have none.
/// </summary>
/// <remarks>
/// The colour and initial are there from the first frame; the picture fills in over them once it
/// has loaded, so a list never waits on a download to draw its people and a picture that cannot be
/// loaded leaves the same avatar everybody had before pictures existed.
/// </remarks>
public sealed partial class AvatarViewModel(string color, string initial) : ObservableObject
{
    public string Color { get; } = color;

    public string Initial { get; } = initial;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    private ImageSource? _image;

    public bool HasImage => Image is not null;
}
