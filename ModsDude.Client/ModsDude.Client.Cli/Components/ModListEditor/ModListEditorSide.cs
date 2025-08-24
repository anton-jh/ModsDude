using Spectre.Console;
using System.Linq;

namespace ModsDude.Client.Cli.Components.ModListEditor;
internal class ModListEditorSide : IFocusable
{
    private readonly List<IListFilter<ModState>> _filters;
    private readonly IEnumerable<ModStateWrapper> _mods;
    private readonly ModViewModelVariant _variant;
    private readonly SelectList<ModViewModel> _selectList;

    private IListFilter<ModState> _selectedFilter;
    private bool _hasFocus;


    public ModListEditorSide(
        IEnumerable<ModStateWrapper> mods,
        List<IListFilter<ModState>> filters,
        ModViewModelVariant variant)
    {
        var allFilter = new AllFilter<ModState>("All");
        _filters = [allFilter, .. filters];
        _selectedFilter = allFilter;

        _mods = mods;
        _variant = variant;
        _selectList = new();
    }


    public bool HasFocus
    {
        get => _hasFocus;
        set
        {
            _hasFocus = value;
            _selectList.HasFocus = value;
        }
    }
    public Layout Layout { get; } = new();


    public void HandleKeyPress(ConsoleKeyInfo consoleKeyInfo)
    {
        switch (consoleKeyInfo.Key)
        {
            case ConsoleKey.Enter:
                ApplyAction(x => x.HandleEnter());
                break;

            case ConsoleKey.Spacebar:
                ApplyAction(x => x.HandleSpacebar());
                break;

            case ConsoleKey.Backspace:
                ApplyAction(x => x.HandleBackspace());
                break;

            case ConsoleKey.Tab:
                _selectedFilter = _filters[(_filters.IndexOf(_selectedFilter) + 1) % _filters.Count];
                break;

            default:
                _selectList.HandleKeyPress(consoleKeyInfo);
                break;
        }
    }

    public void Update()
    {
        var panel = new Panel(_selectList.Update());

        panel.BorderColor(HasFocus
            ? Color.Blue
            : Color.Grey);

        Layout.Update(panel);
    }


    private void ApplyAction(Action<ModViewModel> action)
    {
        var selection = _selectList.Selection;

        if (selection is null)
        {
            return;
        }

        action(selection);
    }

    private IEnumerable<ModViewModel> GetViewModels()
    {
        var states = _mods.Select(x => x.State);
        var filtered = _selectedFilter.Apply(states);

        return _selectedFilter.Apply(_mods.Select(x => x.State))
            .Select(x => new ModViewModel(x, _variant));
    }
}
// now: filters operate on ModState, kinda need to operate on ModStateWrapper instead.
// or, can i get rid of ModStateWrapper?
// filters need to handle filtering AND ordering so maybe call them Views instead?
// NO reason to make filters generic and shit. They're built specifically for this!
