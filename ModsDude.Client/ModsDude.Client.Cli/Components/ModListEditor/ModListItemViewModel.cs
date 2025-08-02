using Spectre.Console;
using Spectre.Console.Rendering;

namespace ModsDude.Client.Cli.Components.ModListEditor;
internal record ModListItemViewModel<T>(T Item, ModListItemState State)
    : IItemViewModel
{
    public IRenderable Render(bool isSelected, bool panelHasFocus)
    {
        return new Markup("Test");
    }
}

internal enum ModListItemState
{
    Unchanged,
    Added,
    Removed,
    Upgraded,
    Downgraded
}
