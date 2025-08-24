using Spectre.Console;
using Spectre.Console.Rendering;

namespace ModsDude.Client.Cli.Components.ModListEditor;

public class SelectList<T>
    where T : class, IItemViewModel
{
    private readonly IEnumerable<T> _itemsSource;
    private List<T> _items;
    private int _selectedIndex = 0;


    public SelectList(IEnumerable<T> itemsSource)
    {
        _itemsSource = itemsSource;
        _items = _itemsSource.ToList();
    }


    public bool HasFocus { get; set; }

    public T? Selection => _items.Count != 0 ? _items[_selectedIndex] : null;
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            _selectedIndex = Math.Max(0, Math.Min(value, _items.Count - 1));
        }
    }


    public void HandleKeyPress(ConsoleKeyInfo consoleKeyInfo)
    {
        switch (consoleKeyInfo.Key)
        {
            case ConsoleKey.UpArrow when _selectedIndex > 0:
                _selectedIndex--;
                break;
            case ConsoleKey.DownArrow when _selectedIndex < _items.Count - 1:
                _selectedIndex++;
                break;
        }
    }

    public IRenderable Update()
    {
        _items = _itemsSource.ToList();
        _selectedIndex = Math.Max(0, Math.Min(_items.Count - 1, _selectedIndex));

        var renderedItems = new List<IRenderable>();

        int totalVisible = 21;
        int half = totalVisible / 2;

        if (_items.Count > 0)
        {
            int start = SelectedIndex - half;
            int end = SelectedIndex + half + 1;

            if (start < 0)
            {
                end += -start;
                start = 0;
            }

            if (end > _items.Count)
            {
                int overshoot = end - _items.Count;
                start -= overshoot;
                end = _items.Count;
                if (start < 0) start = 0;
            }

            for (int i = start; i < end; i++)
            {
                renderedItems.Add(_items[i].Render(SelectedIndex == i, HasFocus));
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
}
