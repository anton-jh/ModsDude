namespace ModsDude.Client.Cli.Components.ModListEditor.Views;

internal class ModListOrderingByName : IModListOrdering
{
    public string Name { get; } = "Name";

    public IEnumerable<ModStateWrapper> Apply(IEnumerable<ModStateWrapper> mods)
    {
        return mods.OrderBy(x => x.State.Mod.DisplayName);
    }
}
