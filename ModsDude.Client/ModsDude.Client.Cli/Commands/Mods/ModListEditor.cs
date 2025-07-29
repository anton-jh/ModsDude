using ModsDude.Client.Core.Models;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;

namespace ModsDude.Client.Cli.Commands.Mods;

public class ModListEditor
{
    private readonly IAnsiConsole _ansiConsole;
    private readonly ObservableCollection<Mod> _availableMods;
    private readonly ObservableCollection<Mod.Version> _activeMods;
    private readonly SelectionInteractiveLivePanel<Mod> _availablePanel;
    private readonly SelectionInteractiveLivePanel<Mod.Version> _activePanel;


    public ModListEditor(
        IEnumerable<Mod> available,
        IEnumerable<Mod.Version> active,
        IAnsiConsole ansiConsole)
    {
        _ansiConsole = ansiConsole;

        _availableMods = new(available);
        _activeMods = new(active);

        _availablePanel = new SelectionInteractiveLivePanel<Mod>(_availableMods, RenderItem);
        _activePanel = new SelectionInteractiveLivePanel<Mod.Version>(_activeMods, RenderItem);

        _availablePanel.SetFocus();
    }


    public void Run()
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
                    var versionToAdd = mod.Latest;

                    if (mod.Versions.Count() == 1)
                    {
                        _availableMods.Remove(mod);
                    }
                    if (_activeMods.FirstOrDefault(x => x.Parent == mod) is Mod.Version activeVersion)
                    {
                        if (activeVersion != versionToAdd)
                        {
                            _activeMods.Remove(activeVersion);
                            _activeMods.Insert(0, versionToAdd);
                        }
                    }
                    else
                    {
                        _activeMods.Insert(0, versionToAdd);
                    }
                }
                else if (_activePanel.HasFocus && _activePanel.Selection is Mod.Version version)
                {
                    _activeMods.Remove(version);
                    if (!_availableMods.Contains(version.Parent))
                    {
                        _availableMods.Insert(0, version.Parent);
                    }
                }
                break;

            default:
                GetActivePanel()?.HandleKeyPress(consoleKeyInfo);
                break;
        }
    }

    private static Markup RenderItem(Mod item, bool isSelected, bool panelHasFocus)
    {
        return new Markup(item.DisplayName,
            (isSelected, panelHasFocus) switch
            {
                (true, true) => Color.Blue,
                (true, false) => Color.Grey,
                _ => Color.Default
            });
    }

    private static Markup RenderItem(Mod.Version item, bool isSelected, bool panelHasFocus)
    {
        return Markup.FromInterpolated($"{item.DisplayName} [italic grey]({item.SequenceNumber})[/]",
            (isSelected, panelHasFocus) switch
            {
                (true, true) => Color.Blue,
                (true, false) => Color.Grey,
                _ => null
            });
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


public class SelectionInteractiveLivePanel<T> : InteractiveLivePanel, IDisposable
    where T : class
{
    private int _selectedIndex = 0;
    private readonly ObservableCollection<T> _items;
    private readonly ItemRenderFunction<T> _itemRenderFunction;

    public delegate IRenderable ItemRenderFunction<TItem>(TItem item, bool isSelected, bool panelHasFocus);


    public SelectionInteractiveLivePanel(ObservableCollection<T> items, ItemRenderFunction<T> itemRenderFunction)
        : base(Render(items, itemRenderFunction, selectedIndex: 0, panelHasFocus: false))
    {
        _items = items;
        _itemRenderFunction = itemRenderFunction;

        _items.CollectionChanged += OnListChanged;
    }


    public T? Selection => _items.Count > 0 ? _items[_selectedIndex] : null;


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
