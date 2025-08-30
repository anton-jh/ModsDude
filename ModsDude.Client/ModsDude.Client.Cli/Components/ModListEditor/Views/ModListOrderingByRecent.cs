namespace ModsDude.Client.Cli.Components.ModListEditor.Views;

internal class ModListOrderingByRecent : IModListOrdering
{
    public string Name { get; } = "Recent";

    public IEnumerable<ModStateWrapper> Apply(IEnumerable<ModStateWrapper> mods)
    {
        var items = mods
            .Select(mod => (Date: GetDate(mod), Mod: mod))
            .OrderByDescending(x => x.Date);

        return items.Select(x => x.Mod);
    }

    private static DateTimeOffset GetDate(ModStateWrapper mod)
    {
        return mod.State switch
        {
            ModState.Added x => x.Version.Created,
            ModState.ChangedVersion x => x.To.Created,
            ModState.Removed x => x.Version.Created,
            var x => x.Mod.Created
        };
    }
}
