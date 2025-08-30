namespace ModsDude.Client.Cli.Components.ModListEditor.Views;

internal class RightDefaultModListView : IModListView
{
    public string Name { get; } = "All";

    public IEnumerable<ModViewModel> Apply(IEnumerable<ModStateWrapper> mods, IModListOrdering ordering)
    {
        var added = mods
            .Where(x => x.State is ModState.Added)
            .ApplyOrdering(ordering);

        var includedOrChanged = mods
            .Where(x => x.State is ModState.Included or ModState.ChangedVersion)
            .ApplyOrdering(ordering);

        IEnumerable<ModStateWrapper> all = [.. added, .. includedOrChanged];
        var viewModels = all.Select(x => new ModViewModel(x, ModViewModelVariant.Right));

        return viewModels;
    }
}
