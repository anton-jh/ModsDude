namespace ModsDude.Client.Cli.Components.ModListEditor.Views;

internal interface IModListOrdering
{
    public string Name { get; }
    IEnumerable<ModStateWrapper> Apply(IEnumerable<ModStateWrapper> mods);
}
