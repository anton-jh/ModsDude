namespace ModsDude.Client.Cli.Components.ModListEditor.Views;

internal class TypeOfModListView<T>(string name, ModViewModelVariant variant) : IModListView
    where T : ModState
{
    public string Name { get; } = name;

    public IEnumerable<ModViewModel> Apply(IEnumerable<ModStateWrapper> mods, IModListOrdering ordering)
    {
        return mods
            .Where(x => x.State is T)
            .ApplyOrdering(ordering)
            .Select(x => new ModViewModel(x, variant));
    }
}
