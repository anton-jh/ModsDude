using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ModsDude.Client.Wpf.Shell.Navigation;

public partial class PageTitle : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(PageTitle),
            new PropertyMetadata(""));

    public static readonly DependencyProperty BackCommandProperty =
        DependencyProperty.Register(
            nameof(BackCommand),
            typeof(ICommand),
            typeof(PageTitle),
            new PropertyMetadata(null));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public ICommand? BackCommand
    {
        get => (ICommand?)GetValue(BackCommandProperty);
        set => SetValue(BackCommandProperty, value);
    }


    public PageTitle()
    {
        InitializeComponent();
    }
}
