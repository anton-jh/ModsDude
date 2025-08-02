using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Core.Models;
using Spectre.Console;
using System.Collections.ObjectModel;
using System.Linq;

namespace ModsDude.Client.Cli.Components.ModListEditor;

public class ModListEditor
{
    private readonly IAnsiConsole _ansiConsole;
    private readonly HashSet<Mod.Version> _originalActive;
    private readonly ObservableCollection<ModListItemViewModel<Mod>> _availableMods;
    private readonly ObservableCollection<ModListItemViewModel<Mod.Version>> _activeMods;
    private readonly SelectableInteractiveLivePanel<ModListItemViewModel<Mod>> _availablePanel;
    private readonly SelectableInteractiveLivePanel<ModListItemViewModel<Mod.Version>> _activePanel;


    public ModListEditor(
        IEnumerable<Mod> available,
        IEnumerable<Mod.Version> active,
        IAnsiConsole ansiConsole)
    {
        _ansiConsole = ansiConsole;

        _originalActive = new(active);

        _activeMods = active
            .Distinct()
            .OrderBy(x => x.DisplayName)
            .Select(x => new ModListItemViewModel<Mod.Version>(x, ModListItemState.Unchanged))
            .Apply(x => new ObservableCollection<ModListItemViewModel<Mod.Version>>(x));

        _availableMods = available
            .Distinct()
            .Where(m => !_activeMods.Any(v => v.Item.Parent == m))
            .OrderBy(x => x.DisplayName)
            .Select(x => new ModListItemViewModel<Mod>(x, ModListItemState.Unchanged))
            .Apply(x => new ObservableCollection<ModListItemViewModel<Mod>>(x));

        _availablePanel = new(_availableMods);
        _activePanel = new(_activeMods);

        _availablePanel.SetFocus();
    }


    public void Start()
    {
        var layout = new Layout()
            .SplitColumns(_availablePanel.Layout, _activePanel.Layout);

        _ansiConsole.Clear();

        _ansiConsole.Live(layout).Start(ctx =>
        {
            while (true)
            {
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
            _availablePanel.SetFocus(false);
            _activePanel.SetFocus(false);
        }

        InteractiveLivePanel? GetActivePanel()
        {
            return new InteractiveLivePanel[]
                {
                    _availablePanel,
                    _activePanel
                }
                .SingleOrDefault(x => x.HasFocus);
        }


        switch (consoleKeyInfo.Key)
        {
            case ConsoleKey.LeftArrow:
                DeFocusAll();
                _availablePanel.SetFocus();
                break;

            case ConsoleKey.RightArrow:
                DeFocusAll();
                _activePanel.SetFocus();
                break;

            case ConsoleKey.Enter:
                if (_availablePanel.HasFocus && _availablePanel.Selection is Mod mod)
                {
                    Activate(mod.Latest);
                }
                else if (_activePanel.HasFocus && _activePanel.Selection is Mod.Version version)
                {
                    Deactivate(version);
                }
                break;

            case ConsoleKey.U when _activePanel.HasFocus:
                IncreaseVersionOfSelectedActive();
                break;

            case ConsoleKey.D when _activePanel.HasFocus:
                DecreaseVersionOfSelectedActive();
                break;

            default:
                GetActivePanel()?.HandleKeyPress(consoleKeyInfo);
                break;
        }
    }

    private void Activate(Mod.Version version)
    {
        _availableMods.Remove(version.Parent);

        var isUndo = _originalActive.Contains(version);
        if (isUndo)
        {
            _activeMods.InsertSorted(version, x => x.DisplayName);
        }
        else
        {
            _activeMods.Insert(0, version);
        }
    }

    private void Deactivate(Mod.Version version)
    {
        _activeMods.Remove(version);

        var isUndo = !_originalActive.Contains(version);
        if (isUndo)
        {
            _availableMods.InsertSorted(version.Parent, x => x.DisplayName);
        }
        else
        {
            _availableMods.Insert(0, version.Parent);
        }
    }

    private void IncreaseVersionOfSelectedActive()
    {
        var version = _activePanel.Selection;
        if (version is null)
        {
            return;
        }

        var nextVersion = version.Parent.Versions
            .Where(x => x.SequenceNumber > version.SequenceNumber)
            .MinBy(x => x.SequenceNumber);
        if (nextVersion is null)
        {
            return;
        }

        _activeMods.Remove(version);
        if (_originalActive.Contains(nextVersion))
        {
            _activeMods.InsertSorted(nextVersion, x => x.DisplayName);
        }
        else
        {
            _activeMods.Insert(0, nextVersion);
        }
        _activePanel.SelectedIndex = _activeMods.IndexOf(nextVersion);
    }

    private void DecreaseVersionOfSelectedActive()
    {
        var version = _activePanel.Selection;
        if (version is null)
        {
            return;
        }

        var prevVersion = version.Parent.Versions
            .Where(x => x.SequenceNumber < version.SequenceNumber)
            .MaxBy(x => x.SequenceNumber);
        if (prevVersion is null)
        {
            return;
        }

        _activeMods.Remove(version);
        if (_originalActive.Contains(prevVersion))
        {
            _activeMods.InsertSorted(prevVersion, x => x.DisplayName);
        }
        else
        {
            _activeMods.Insert(0, prevVersion);
        }
        _activePanel.SelectedIndex = _activeMods.IndexOf(prevVersion);
    }


    private Markup RenderItem(Mod item, bool isSelected, bool panelHasFocus)
    {
        var foreground = (isSelected, panelHasFocus) switch
        {
            (true, true) => Color.Blue,
            (true, false) => Color.Grey,
            _ => Color.Default
        };
        string changed = "";
        if (_originalActive.Any(x => x.Parent == item))
        {
            changed = "[grey italic](-)[/] ";
        }

        return new Markup($"{changed}{item.DisplayName.EscapeMarkup()}",
            new Style(foreground: foreground));
    }

    private Markup RenderItem(Mod.Version item, bool isSelected, bool panelHasFocus)
    {
        var foreground = (isSelected, panelHasFocus) switch
        {
            (true, true) => Color.Blue,
            (true, false) => Color.Grey,
            _ => Color.Default
        };
        string changed = "";
        if (!_originalActive.Contains(item))
        {
            var sign = _originalActive.FirstOrDefault(x => x.Parent == item.Parent) switch
            {
                Mod.Version originalVersion when item.SequenceNumber > originalVersion.SequenceNumber
                    => ">",
                Mod.Version originalVersion when item.SequenceNumber < originalVersion.SequenceNumber
                    => "<",
                _ => "+"
            };
            changed = $"[grey italic]({sign})[/] ";
        }

        return new Markup($"{changed}{item.DisplayName.EscapeMarkup()}[italic grey]({item.SequenceNumber})[/]",
            new Style(foreground: foreground));
    }
}
