using ModsDude.Client.Wpf.Account;
using System.Windows;
using System.Windows.Controls;

namespace ModsDude.Client.Wpf.Shell.Sidebar;

/// <summary>The account's avatar for the account entry, and a tile for every other rail entry.</summary>
public sealed class RailItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Item { get; set; }

    public DataTemplate? Account { get; set; }


    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        => item is AccountItemViewModel ? Account : Item;
}
