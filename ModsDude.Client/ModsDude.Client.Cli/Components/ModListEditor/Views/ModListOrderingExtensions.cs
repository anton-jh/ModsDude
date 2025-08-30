namespace ModsDude.Client.Cli.Components.ModListEditor.Views;

internal static class ModListOrderingExtensions
{
    public static IEnumerable<ModStateWrapper> ApplyOrdering(this IEnumerable<ModStateWrapper> mods, IModListOrdering ordering)
    {
        return ordering.Apply(mods);
    }
}
