using System.Windows;
using System.Windows.Controls;

namespace ModsDude.Client.Wpf.Shell.Navigation;

public partial class PageTitle : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(PageTitle),
            new PropertyMetadata(""));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }


    public PageTitle()
    {
        InitializeComponent();
    }
}
