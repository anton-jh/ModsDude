using System.Net.NetworkInformation;

namespace ModsDude.Client.Cli.Components.ModListEditor.Views;

internal class LeftDefaultModListView : IModListView
{
    public string Name { get; } = "All";

    public IEnumerable<ModViewModel> Apply(IEnumerable<ModStateWrapper> mods, IModListOrdering ordering)
    {
        var removed = mods
            .Where(x => x.State is ModState.Removed)
            .ApplyOrdering(ordering);

        var updates = mods
            .Where(x => x.State is ModState.Included { UpdateAvailable: true })
            .ApplyOrdering(ordering);

        var available = mods
            .Where(x => x.State is ModState.Available)
            .ApplyOrdering(ordering);

        IEnumerable<ModStateWrapper> all = [.. removed, .. updates, .. available];
        var viewModels = all.Select(x => new ModViewModel(x, ModViewModelVariant.Left));

        return viewModels;
    }
}
