using Spectre.Console.Rendering;

namespace ModsDude.Client.Cli.Components.ModListEditor;
public interface IItemViewModel
{
    IRenderable Render(bool isSelected, bool panelHasFocus);
}
