namespace ModsDude.Client.Cli.Components.ModListEditor;

internal interface IListFilter<T>
{
    string DisplayName { get; }
    IEnumerable<T> Apply(IEnumerable<T> source);
}

internal class TypeOfFilter<TBase, TFilter>(string displayName, Func<TFilter, bool>? extraFilter = null)
    : IListFilter<TBase>
    where TFilter : class, TBase
{
    private readonly Func<TFilter, bool>? _extraFilter = extraFilter;


    public string DisplayName { get; } = displayName;


    public IEnumerable<TBase> Apply(IEnumerable<TBase> source)
    {
        var filtered = source.OfType<TFilter>();

        if (_extraFilter is not null)
        {
            filtered = filtered.Where(_extraFilter);
        }

        return filtered;
    }
}

internal class AllFilter<T>(string displayName)
    : IListFilter<T>
{
    public string DisplayName { get; } = displayName;


    public IEnumerable<T> Apply(IEnumerable<T> source)
    {
        return source;
    }
}
