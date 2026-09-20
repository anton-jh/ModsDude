using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ModsDude.Client.Wpf.View.UserControls;

public partial class ToastHost : UserControl
{
    public ToastHost()
    {
        InitializeComponent();
    }


    // Handlers rather than bindings: IsMouseOver is read-only, so it cannot be pushed into the view
    // model, and holding a toast's clock while it is pointed at is the whole of what these do.
    private void OnToastMouseEnter(object sender, MouseEventArgs e)
    {
        (((FrameworkElement)sender).DataContext as ToastViewModel)?.Pause();
    }

    private void OnToastMouseLeave(object sender, MouseEventArgs e)
    {
        (((FrameworkElement)sender).DataContext as ToastViewModel)?.Resume();
    }
}
