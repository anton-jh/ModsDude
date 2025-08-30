namespace ModsDude.Client.Cli.Components.ModListEditor.Views;
internal interface IModListView
{
    public string Name { get; }
    IEnumerable<ModViewModel> Apply(IEnumerable<ModStateWrapper> mods, IModListOrdering ordering);
}
