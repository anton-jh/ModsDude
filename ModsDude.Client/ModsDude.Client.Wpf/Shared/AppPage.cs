using System.Windows.Controls;

namespace ModsDude.Client.Wpf.Shared;

/// <summary>
/// The base of every page. Implicit styles match the exact type, so a page subclass would otherwise miss
/// the theme's Page style, and with it the text colour a Frame does not pass down.
/// </summary>
public class AppPage : Page
{
    public AppPage()
    {
        SetResourceReference(StyleProperty, typeof(Page));
    }
}
