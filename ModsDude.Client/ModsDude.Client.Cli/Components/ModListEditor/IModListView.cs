namespace ModsDude.Client.Cli.Components.ModListEditor;
internal interface IModListView
{
    public string Name { get; }
    IEnumerable<ModViewModel> Apply(IEnumerable<ModStateWrapper> mods, IModListOrdering ordering);
}

internal class LeftDefaultModListView : IModListView
{
    public string Name { get; } = "All";

    public IEnumerable<ModViewModel> Apply(IEnumerable<ModStateWrapper> mods, IModListOrdering ordering)
    {
        var removed = ordering.Apply(mods.Where(x => x.State is ModState.Removed));
        var updates = ordering.Apply(mods.Where(x => x.State is ModState.Included { UpdateAvailable: true }));
        var available = ordering.Apply(mods.Where(x => x.State is ModState.Available));

        IEnumerable<ModStateWrapper> all = [.. removed, .. updates, .. available];
        var viewModels = all.Select(x => new ModViewModel(x, ModViewModelVariant.Left));

        return viewModels;
    }
}

internal interface IModListOrdering
{
    public string Name { get; }
    IEnumerable<ModStateWrapper> Apply(IEnumerable<ModStateWrapper> mods);
}

internal class ModListOrderingByName : IModListOrdering
{
    public string Name { get; } = "Name";

    public IEnumerable<ModStateWrapper> Apply(IEnumerable<ModStateWrapper> mods)
    {
        return mods.OrderBy(x => x.State.Mod.DisplayName);
    }
}

internal class ModListOrderingByRecent : IModListOrdering
{
    public string Name { get; } = "Recent";

    public IEnumerable<ModStateWrapper> Apply(IEnumerable<ModStateWrapper> mods)
    {
        return mods.OrderByDescending(x => x.State.Mod.) // how to sort by recent? import-date works for already imported mods / versions, but how about not-yet-imported mods when selecting from local folder? maybe just set current time for those...
    }
}
