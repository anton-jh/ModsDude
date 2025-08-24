namespace ModsDude.Client.Cli.Components.ModListEditor;
internal class MultiFocusBehaviour
{
    private readonly HashSet<IFocusable> _focusables;


    public MultiFocusBehaviour(IEnumerable<IFocusable> focusables, IFocusable defaultFocusable)
    {
        _focusables = focusables.ToHashSet();
        Current = defaultFocusable;

        Focus(defaultFocusable);
    }

    internal IFocusable Current { get; private set; }

    public void Focus(IFocusable focusable)
    {
        foreach (var item in _focusables)
        {
            item.HasFocus = false;
        }
        Current = focusable;
    }
}
