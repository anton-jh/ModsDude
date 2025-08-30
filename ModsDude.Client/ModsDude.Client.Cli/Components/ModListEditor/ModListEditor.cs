using ModsDude.Client.Cli.Components.ModListEditor.Views;
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

        List<IModListOrdering> orderings = [
            new ModListOrderingByName(),
            new ModListOrderingByRecent()
        ];

        List<IModListView> leftViews = [
            new LeftDefaultModListView(),
            new TypeOfModListView<ModState.Available>("Available", ModViewModelVariant.Left),
            new UpdatesModListView(ModViewModelVariant.Left),
            new TypeOfModListView<ModState.Removed>("Removed", ModViewModelVariant.Left)
        ];
        _leftSide = new ModListEditorSide(_mods, leftViews, orderings);

        List<IModListView> rightViews = [
            new RightDefaultModListView(),
            new UpdatesModListView(ModViewModelVariant.Right),
            new TypeOfModListView<ModState.ChangedVersion>("Changed", ModViewModelVariant.Right),
            new TypeOfModListView<ModState.Added>("Added", ModViewModelVariant.Right)
        ];
        _rightSide = new ModListEditorSide(_mods, rightViews, orderings);

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
