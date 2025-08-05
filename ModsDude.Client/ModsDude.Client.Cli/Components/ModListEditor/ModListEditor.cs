using Microsoft.Identity.Client;
using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Core.Models;
using Spectre.Console;
using System.Collections.ObjectModel;
using System.Linq;

namespace ModsDude.Client.Cli.Components.ModListEditor;

public class ModListEditor
{
    private readonly IAnsiConsole _ansiConsole;

    private readonly HashSet<ModState> _mods;

    private readonly IEnumerable<ModViewModel> _excludedView;
    private readonly IEnumerable<ModViewModel> _includedView;
    private readonly SelectableInteractiveLivePanel<ModViewModel> _leftPanel;
    private readonly SelectableInteractiveLivePanel<ModViewModel> _rightPanel;

    private RightFilter _rightFilter = RightFilter.All;
    private LeftFilter _leftFilter = LeftFilter.All;


    public enum Change
    {
        Added,
        Removed,
        ChangedVersion
    }

    public enum RightFilter
    {
        All,
        Included,
        Added,
        ChangedVersion
    }

    public enum LeftFilter
    {
        All,
        Available,
        UpdateAvailable,
        Removed
    }



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

        _mods = new(Enumerable.Concat<ModState>(availableStates, includedStates));

        _includedView = ProjectRight();
        _excludedView = ProjectLeft();

        _leftPanel = new(_excludedView);
        _rightPanel = new(_includedView);

        _leftPanel.SetFocus();
    }


    private IEnumerable<ModViewModel> ProjectRight()
    {
        var included = _mods
            .OfType<ModState.Included>()
            .Select(x => new ModViewModel(x, ModViewModelVariant.Right))
            .OrderBy(x => x.Name);

        var added = _mods
            .OfType<ModState.Added>()
            .Select(x => new ModViewModel(x, ModViewModelVariant.Right))
            .OrderBy(x => x.Name);

        var changedVersion = _mods
            .OfType<ModState.ChangedVersion>()
            .Select(x => new ModViewModel(x, ModViewModelVariant.Right))
            .OrderBy(x => x.Name);

        IEnumerable<ModViewModel> items = _rightFilter switch
        {
            RightFilter.Included => included,

            RightFilter.Added => added,

            RightFilter.ChangedVersion => changedVersion,

            _ => [..added, ..changedVersion, ..included]
        };

        foreach (var item in items)
        {
            yield return item;
        }
    }

    private IEnumerable<ModViewModel> ProjectLeft()
    {
        var available = _mods
            .OfType<ModState.Available>()
            .Select(x => new ModViewModel(x, ModViewModelVariant.Left))
            .OrderBy(x => x.Name);

        var removed = _mods
            .OfType<ModState.Removed>()
            .Select(x => new ModViewModel(x, ModViewModelVariant.Left))
            .OrderBy(x => x.Name);

        var updates = _mods
            .OfType<ModState.Included>()
            .Where(x => x.UpdateAvailable)
            .Select(x => new ModViewModel(x, ModViewModelVariant.Left))
            .OrderBy(x => x.Name);

        IEnumerable<ModViewModel> items = _leftFilter switch
        {
            LeftFilter.Available => available,

            LeftFilter.Removed => removed,

            LeftFilter.UpdateAvailable => updates,

            _ => [.. removed, .. updates, .. available]
        };

        foreach (var item in items)
        {
            yield return item;
        }
    }
    

    public void Start()
    {
        var layout = new Layout()
            .SplitColumns(_leftPanel.Layout, _rightPanel.Layout);

        _ansiConsole.Clear();

        _ansiConsole.Live(layout).Start(ctx =>
        {
            while (true)
            {
                _rightPanel.Update();
                _leftPanel.Update();
                ctx.Refresh();
                var key = Console.ReadKey(true);
                HandleKeyPress(key);
            }
        });
    }


    private void HandleKeyPress(ConsoleKeyInfo consoleKeyInfo)
    {
        void DeFocusAll()
        {
            _leftPanel.SetFocus(false);
            _rightPanel.SetFocus(false);
        }


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
                GetActivePanel()?.HandleKeyPress(consoleKeyInfo);
                break;
        }
    }

    private SelectableInteractiveLivePanel<ModViewModel>? GetActivePanel()
    {
        return new SelectableInteractiveLivePanel<ModViewModel>[]
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
