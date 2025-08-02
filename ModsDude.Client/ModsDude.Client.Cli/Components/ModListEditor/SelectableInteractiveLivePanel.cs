using Spectre.Console;
using Spectre.Console.Rendering;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace ModsDude.Client.Cli.Components.ModListEditor;

public class SelectableInteractiveLivePanel<T> : InteractiveLivePanel, IDisposable
    where T : class, IItemViewModel
{
    private int _selectedIndex = 0;
    private readonly ObservableCollection<T> _items;


    public SelectableInteractiveLivePanel(ObservableCollection<T> items)
        : base(Render(items, selectedIndex: 0, panelHasFocus: false))
    {
        _items = items;

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
        Update(Render(_items, _selectedIndex, HasFocus));
    }

    private void OnListChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _selectedIndex = Math.Max(0, Math.Min(_items.Count - 1, _selectedIndex));
        Update();
    }


    private static Rows Render(ObservableCollection<T> items, int selectedIndex, bool panelHasFocus)
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
                renderedItems.Add(items[i].Render(selectedIndex == i, panelHasFocus));
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
        Update(Render(_items, _selectedIndex, HasFocus));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _items.CollectionChanged -= OnListChanged;
    }
}
