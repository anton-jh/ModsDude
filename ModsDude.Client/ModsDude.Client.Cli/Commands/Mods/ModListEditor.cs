using ModsDude.Client.Cli.Extensions;
using ModsDude.Client.Core.Models;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace ModsDude.Client.Cli.Commands.Mods;

public class ModListEditor
{
    private readonly IAnsiConsole _ansiConsole;
    private readonly HashSet<Mod.Version> _originalActive;
    private readonly ObservableCollection<Mod> _availableMods;
    private readonly ObservableCollection<Mod.Version> _activeMods;
    private readonly SelectableInteractiveLivePanel<Mod> _availablePanel;
    private readonly SelectableInteractiveLivePanel<Mod.Version> _activePanel;


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
            .Apply(x => new ObservableCollection<Mod.Version>(x));

        _availableMods = available
            .Distinct()
            .Where(m => !_activeMods.Any(v => v.Parent == m))
            .OrderBy(x => x.DisplayName)
            .Apply(x => new ObservableCollection<Mod>(x));

        _availablePanel = new SelectableInteractiveLivePanel<Mod>(_availableMods, RenderItem);
        _activePanel = new SelectableInteractiveLivePanel<Mod.Version>(_activeMods, RenderItem);

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


public abstract class InteractiveLivePanel
{
    private IRenderable _renderable;
    private Panel _panel;


    protected InteractiveLivePanel(IRenderable renderable)
    {
        _renderable = new Markup("");
        _panel = new(renderable);
        Layout = new(_panel);
        Update();
    }


    public bool HasFocus { get; private set; }
    public Layout Layout { get; protected set; }


    public void SetFocus(bool focus = true)
    {
        HasFocus = focus;
        Update();
        OnFocusChanged();
    }


    protected void Update(IRenderable? renderable = null)
    {
        if (renderable is not null && renderable != _renderable)
        {
            _renderable = renderable;
            _panel = new(_renderable);
            Layout.Update(_panel);
        }

        _panel.BorderColor(HasFocus
            ? Color.Blue
            : Color.Grey);
    }

    public abstract void HandleKeyPress(ConsoleKeyInfo consoleKeyInfo);
    protected abstract void OnFocusChanged();
}


public class SelectableInteractiveLivePanel<T> : InteractiveLivePanel, IDisposable
    where T : class
{
    private int _selectedIndex = 0;
    private readonly ObservableCollection<T> _items;
    private readonly ItemRenderFunction<T> _itemRenderFunction;

    public delegate IRenderable ItemRenderFunction<TItem>(TItem item, bool isSelected, bool panelHasFocus);


    public SelectableInteractiveLivePanel(ObservableCollection<T> items, ItemRenderFunction<T> itemRenderFunction)
        : base(Render(items, itemRenderFunction, selectedIndex: 0, panelHasFocus: false))
    {
        _items = items;
        _itemRenderFunction = itemRenderFunction;

        _items.CollectionChanged += OnListChanged;
    }


    public T? Selection => _items.Count > 0 ? _items[_selectedIndex] : null;
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            _selectedIndex = Math.Max(0, Math.Min(value, _items.Count - 1));
            Update();
        }
    }


    public override void HandleKeyPress(ConsoleKeyInfo consoleKeyInfo)
    {
        switch (consoleKeyInfo.Key)
        {
            case ConsoleKey.UpArrow when _selectedIndex > 0:
                _selectedIndex--;
                Update();
                break;
            case ConsoleKey.DownArrow when _selectedIndex < _items.Count - 1:
                _selectedIndex++;
                Update();
                break;
        }
    }


    private void Update()
    {
        Update(Render(_items, _itemRenderFunction, _selectedIndex, HasFocus));
    }

    private void OnListChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _selectedIndex = Math.Max(0, Math.Min(_items.Count - 1, _selectedIndex));
        Update();
    }


    private static Rows Render(ObservableCollection<T> items, ItemRenderFunction<T> itemRenderFunction, int selectedIndex, bool panelHasFocus)
    {
        var renderedItems = new List<IRenderable>(9);

        int totalVisible = 21;
        int half = totalVisible / 2;

        if (items.Count > 0)
        {
            int start = selectedIndex - half;
            int end = selectedIndex + half + 1;

            if (start < 0)
            {
                end += -start;
                start = 0;
            }

            if (end > items.Count)
            {
                int overshoot = end - items.Count;
                start -= overshoot;
                end = items.Count;
                if (start < 0) start = 0;
            }

            for (int i = start; i < end; i++)
            {
                renderedItems.Add(itemRenderFunction(items[i], selectedIndex == i, panelHasFocus));
            }
        }

        while (renderedItems.Count < totalVisible)
        {
            renderedItems.Add(new Text(" "));
        }

        var rows = new Rows(renderedItems);
        rows.Expand();

        return rows;
    }



    protected override void OnFocusChanged()
    {
        Update(Render(_items, _itemRenderFunction, _selectedIndex, HasFocus));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _items.CollectionChanged -= OnListChanged;
    }
}
