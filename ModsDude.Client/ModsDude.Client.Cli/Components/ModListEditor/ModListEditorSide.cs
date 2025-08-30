using ModsDude.Client.Cli.Components.ModListEditor.Views;
using Spectre.Console;

namespace ModsDude.Client.Cli.Components.ModListEditor;
internal class ModListEditorSide : IFocusable
{
    private readonly List<IModListView> _views;
    private readonly List<IModListOrdering> _orderings;
    private readonly IEnumerable<ModStateWrapper> _mods;
    private readonly SelectList<ModViewModel> _selectList;
    private readonly Layout _headerLayout;
    private readonly Layout _listLayout;

    private IModListView _selectedView;
    private IModListOrdering _selectedOrdering;
    private bool _hasFocus;


    public ModListEditorSide(
        IEnumerable<ModStateWrapper> mods,
        List<IModListView> views,
        List<IModListOrdering> orderings)
    {
        _views = views;
        _selectedView = _views.First();
        _orderings = orderings;
        _selectedOrdering = _orderings.First();

        _mods = mods;
        _selectList = new(GetViewModels());

        _headerLayout = new Layout() { Size = 2 };
        _listLayout = new Layout();
        Layout = new Layout().SplitRows(
            _headerLayout,
            _listLayout);
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
    public Layout Layout { get; }


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

            case ConsoleKey.Tab when consoleKeyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift) == false:
                _selectedView = _views[(_views.IndexOf(_selectedView) + 1) % _views.Count];
                break;

            case ConsoleKey.Tab when consoleKeyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift) == true:
                _selectedOrdering = _orderings[(_orderings.IndexOf(_selectedOrdering) + 1) % _orderings.Count];
                break;

            default:
                _selectList.HandleKeyPress(consoleKeyInfo);
                break;
        }
    }

    public void Update()
    {
        _headerLayout.Update(RenderHeader());

        var body = new Panel(_selectList.Update());
        body.BorderColor(HasFocus
            ? Color.Blue
            : Color.Grey);
        _listLayout.Update(body);
    }


    private Padder RenderHeader()
    {
        var markup = new Markup($"[grey][[TAB]][/] {_selectedView.Name}");

        return new Padder(markup, new Padding(2, 1, 2, 0));
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
        var items = _selectedView.Apply(_mods, _selectedOrdering);

        foreach (var item in items)
        {
            yield return item;
        }
    }
}
