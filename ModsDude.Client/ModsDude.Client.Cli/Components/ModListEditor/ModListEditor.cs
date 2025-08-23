using ModsDude.Client.Core.Models;
using Spectre.Console;

namespace ModsDude.Client.Cli.Components.ModListEditor;

public class ModListEditor
{
    private readonly IAnsiConsole _ansiConsole;

    private readonly HashSet<ModState> _mods;

    private readonly IEnumerable<ModViewModel> _excludedView;
    private readonly IEnumerable<ModViewModel> _includedView;
    private readonly SelectableInteractiveListPanel<ModViewModel> _leftPanel;
    private readonly SelectableInteractiveListPanel<ModViewModel> _rightPanel;
    private readonly List<IListFilter<ModState>> _leftFilters;
    private readonly List<IListFilter<ModState>> _rightFilters;

    private IListFilter<ModState> _selectedLeftFilter;
    private IListFilter<ModState> _selectedRightFilter;



    public ModListEditor(
        IEnumerable<Mod> available,
        IEnumerable<Mod.Version> included,
        IAnsiConsole ansiConsole)
    {
        _ansiConsole = ansiConsole;

        var availableStates = available
            .Except(included.Select(x => x.Parent))
            .Select(x => new ModState.Available(x));

        var includedStates = included
            .Select(x => new ModState.Included(x));

        _mods = Enumerable.Concat<ModState>(availableStates, includedStates).ToHashSet();

        var allFilter = new AllFilter<ModState>("All");
        _selectedLeftFilter = allFilter;
        _leftFilters = [
            allFilter,
            new TypeOfFilter<ModState, ModState.Available>("Available"),
            new TypeOfFilter<ModState, ModState.Included>("Updates", x => x.UpdateAvailable),
            new TypeOfFilter<ModState, ModState.Removed>("Removed")
        ];
        _selectedRightFilter = allFilter;
        _rightFilters = [
            allFilter,
            new TypeOfFilter<ModState, ModState.Included>("Existing"),
            new TypeOfFilter<ModState, ModState.Included>("Updates", x => x.UpdateAvailable),
            new TypeOfFilter<ModState, ModState.ChangedVersion>("Changed"),
            new TypeOfFilter<ModState, ModState.Added>("Added"),
        ];

        _includedView = ProjectRight();
        _excludedView = ProjectLeft();

        _leftPanel = new(_excludedView);
        _rightPanel = new(_includedView);

        _leftPanel.SetFocus();
    }


    private IEnumerable<ModViewModel> ProjectRight()
    {
        var items = _selectedRightFilter.Apply(_mods)
            .Select(x => new ModViewModel(x, ModViewModelVariant.Right))
            .OrderBy(x => x.Name);

        foreach (var item in items)
        {
            yield return item;
        }
    }

    private IEnumerable<ModViewModel> ProjectLeft()
    {
        var items = _selectedLeftFilter.Apply(_mods)
            .Select(x => new ModViewModel(x, ModViewModelVariant.Left))
            .OrderBy(x => x.Name);

        foreach (var item in items)
        {
            yield return item;
        }
    }
    

    public void Start()
    {
        var leftLayout = new Layout();
        var leftTitleLayout = new Layout();
        leftLayout.SplitRows(leftTitleLayout, _leftPanel.Layout);

        var layout = new Layout()
            .SplitColumns(leftLayout, _rightPanel.Layout);

        _ansiConsole.Clear();

        _ansiConsole.Live(layout).Start(ctx =>
        {
            while (true)
            {
                leftTitleLayout.Update(new Markup($"[italic gray][[TAB]][/] {_selectedLeftFilter.DisplayName.EscapeMarkup()}"));
                _leftPanel.Update();
                _rightPanel.Update();
                ctx.Refresh();
                var key = Console.ReadKey(true);
                HandleKeyPress(key);
            }
        });
    }


    private void HandleKeyPress(ConsoleKeyInfo consoleKeyInfo)
    {
        var activePanel = GetActivePanel();

        switch (consoleKeyInfo.Key)
        {
            case ConsoleKey.LeftArrow:
                DeFocusAll();
                _leftPanel.SetFocus();
                break;

            case ConsoleKey.RightArrow:
                DeFocusAll();
                _rightPanel.SetFocus();
                break;

            case ConsoleKey.Tab:
                if (activePanel == _leftPanel)
                {
                    _selectedLeftFilter = _leftFilters[(_leftFilters.IndexOf(_selectedLeftFilter) + 1) % _leftFilters.Count];
                }
                else if (activePanel == _rightPanel)
                {
                    _selectedRightFilter = _rightFilters[(_rightFilters.IndexOf(_selectedRightFilter) + 1) % _rightFilters.Count];
                }
                break;

            case ConsoleKey.Enter:
                SelectMod(x => x.HandleEnter());
                break;

            case ConsoleKey.Backspace:
                SelectMod(x => x.HandleBackspace());
                break;

            case ConsoleKey.Spacebar:
                SelectMod(x => x.HandleSpacebar());
                break;
                
            default:
                activePanel?.HandleKeyPress(consoleKeyInfo);
                break;
        }
    }

    private void DeFocusAll()
    {
        _leftPanel.SetFocus(false);
        _rightPanel.SetFocus(false);
    }

    private SelectableInteractiveListPanel<ModViewModel>? GetActivePanel()
    {
        return new SelectableInteractiveListPanel<ModViewModel>[]
            {
                _leftPanel,
                _rightPanel
            }
            .SingleOrDefault(x => x.HasFocus);
    }

    private void SelectMod(Func<ModViewModel, ModState> function)
    {
        var selection = GetActivePanel()?.Selection;

        if (selection is null)
        {
            return;
        }

        var oldState = selection.State;
        var newState = function(selection);

        if (newState != oldState)
        {
            _mods.Remove(oldState);
            _mods.Add(newState);
        }
    }
}
