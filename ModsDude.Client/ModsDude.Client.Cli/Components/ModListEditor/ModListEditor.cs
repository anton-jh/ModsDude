using ModsDude.Client.Core.Models;
using Spectre.Console;

namespace ModsDude.Client.Cli.Components.ModListEditor;

public class ModListEditor
{
    private readonly IAnsiConsole _ansiConsole;
    private readonly MultiFocusBehaviour _multiFocusBehaviour;
    private readonly HashSet<ModStateWrapper> _mods;
    private readonly ModListEditorSide _leftSide;
    private readonly ModListEditorSide _rightSide;

    
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

        _mods = Enumerable.Concat<ModState>(availableStates, includedStates)
            .Select(x => new ModStateWrapper(x))
            .ToHashSet();

        List<IListFilter<ModState>> leftFilters = [
            new TypeOfFilter<ModState, ModState.Available>("Available"),
            new TypeOfFilter<ModState, ModState.Included>("Updates", x => x.UpdateAvailable),
            new TypeOfFilter<ModState, ModState.Removed>("Removed")
        ];
        _leftSide = new ModListEditorSide(_mods, leftFilters, ModViewModelVariant.Left);

        List<IListFilter<ModState>> rightFilters = [
            new TypeOfFilter<ModState, ModState.Included>("Existing"),
            new TypeOfFilter<ModState, ModState.Included>("Updates", x => x.UpdateAvailable),
            new TypeOfFilter<ModState, ModState.ChangedVersion>("Changed"),
            new TypeOfFilter<ModState, ModState.Added>("Added"),
        ];
        _rightSide = new ModListEditorSide(_mods, rightFilters, ModViewModelVariant.Right);

        _multiFocusBehaviour = new([_leftSide, _rightSide], _leftSide);
    }
    

    public void Start()
    {
        var layout = new Layout()
            .SplitColumns(_leftSide.Layout, _rightSide.Layout);

        _ansiConsole.Clear();

        _ansiConsole.Live(layout).Start(ctx =>
        {
            while (true)
            {
                _leftSide.Update();
                _rightSide.Update();
                ctx.Refresh();
                var key = Console.ReadKey(true);
                HandleKeyPress(key);
            }
        });
    }


    private void HandleKeyPress(ConsoleKeyInfo consoleKeyInfo)
    {
        var activePanel = _multiFocusBehaviour.Current;

        switch (consoleKeyInfo.Key)
        {
            case ConsoleKey.LeftArrow:
                _multiFocusBehaviour.Focus(_leftSide);
                break;

            case ConsoleKey.RightArrow:
                _multiFocusBehaviour.Focus(_rightSide);
                break;

            default:
                activePanel.HandleKeyPress(consoleKeyInfo);
                break;
        }
    }
}
