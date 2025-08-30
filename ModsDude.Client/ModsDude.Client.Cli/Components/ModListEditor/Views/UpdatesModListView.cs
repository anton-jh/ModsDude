namespace ModsDude.Client.Cli.Components.ModListEditor.Views;

internal class UpdatesModListView(ModViewModelVariant variant) : IModListView
{
    public string Name { get; } = "Updates";

    public IEnumerable<ModViewModel> Apply(IEnumerable<ModStateWrapper> mods, IModListOrdering ordering)
    {
        return mods
            .Where(x => x.State is ModState.Included { UpdateAvailable: true })
            .ApplyOrdering(ordering)
            .Select(x => new ModViewModel(x, variant));
    }
}
