using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ModsDude.Client.Wpf.View.UserControls;

/// <summary>
/// One person, in a circle. See the XAML for what is drawn; this only keeps the circle a circle.
/// </summary>
public partial class UserAvatar : UserControl
{
    public UserAvatar()
    {
        InitializeComponent();

        SizeChanged += OnSizeChanged;
    }


    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var radius = Math.Min(e.NewSize.Width, e.NewSize.Height) / 2;

        Root.Clip = new EllipseGeometry(new Point(e.NewSize.Width / 2, e.NewSize.Height / 2), radius, radius);
    }
}
