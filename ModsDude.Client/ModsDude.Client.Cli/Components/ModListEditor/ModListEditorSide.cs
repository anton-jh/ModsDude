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
    private readonly Layout _footerLayout;

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
        _footerLayout = new Layout() { Size = 5 };
        _listLayout = new Layout();
        Layout = new Layout().SplitRows(
            _headerLayout,
            _listLayout,
            _footerLayout);
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
            case ConsoleKey.D:
                _selectedView = _views[(_views.IndexOf(_selectedView) + 1) % _views.Count];
                break;

            case ConsoleKey.A:
                _selectedView = _views[(_views.IndexOf(_selectedView) - 1 + _views.Count) % _views.Count];
                break;

            case ConsoleKey.S:
                _selectedOrdering = _orderings[(_orderings.IndexOf(_selectedOrdering) + 1) % _orderings.Count];
                break;

            default:
                if (_selectList.Selection?.HandleKeyPress(consoleKeyInfo) == true)
                {
                    break;
                }
                _selectList.HandleKeyPress(consoleKeyInfo);
                break;
        }
    }

    public void Update()
    {
        var body = new Panel(_selectList.Update());
        body.BorderColor(HasFocus
            ? Color.Blue
            : Color.Grey);

        _headerLayout.Update(RenderHeader());
        _footerLayout.Update(RenderFooter());
        _listLayout.Update(body);
    }


    private Padder RenderHeader()
    {
        var markup = new Markup($"[grey][[A/D]][/] {_selectedView.Name.PadRight(_views.Max(x => x.Name.Length))}  " +
            $"[grey][[S]][/] {_selectedOrdering.Name}");
        return new Padder(markup, new Padding(2, 1, 2, 0));
    }

    private Padder RenderFooter()
    {
        var actions = _selectList.Selection?.GetPossibleActions() ?? [];

        var grid = new Grid()
            .AddColumns(2);

        foreach (var action in actions)
        {
            grid.AddRow(new Markup($"[grey][[{action.Key}]][/]"), new Markup(action.Label));
        }

        return new Padder(grid, new Padding(2, 0, 2, 0));
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
