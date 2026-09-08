using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections;
using System.ComponentModel;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>A row that can be picked out of a list, whichever kind of row it is.</summary>
public interface ISelectableRow : INotifyPropertyChanged
{
    bool IsSelected { get; set; }
}

/// <summary>
/// What a list's mouse and keyboard gestures do to a selection. The view talks to this and to
/// nothing else, so the same behaviour drives both of the editor's lists.
/// </summary>
public interface IListSelection
{
    /// <summary>A plain click: this row and nothing else.</summary>
    void Click(object? item);

    /// <summary>Ctrl-click, or the space bar: this row joins or leaves the selection.</summary>
    void Toggle(object? item);

    /// <summary>Shift-click: everything from the anchor to here, in the order the list shows.</summary>
    void ExtendTo(object? item);

    /// <summary>
    /// Right-clicking a row that is not in the selection selects it, the way Explorer does - a
    /// context menu has to be about something the user can see is picked.
    /// </summary>
    void EnsureSelected(object? item);

    void SelectAllShown();

    void ClearSelection();

    /// <summary>
    /// Enter, or a double click. Given a row, that row unless it is part of the selection - double
    /// clicking one of five picked rows means the five, not the one under the pointer.
    /// </summary>
    void Activate(object? item);
}

/// <summary>
/// A selection over a filtered list: the rows own the flag, this owns the gestures, and the list
/// control owns neither.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the rows own the flag.</b> A <see cref="System.Windows.Controls.ListBox"/> maintains its
/// own selection, and drops from it anything a collection view filters out - which would mean typing
/// in the search box silently discarded the selection it was narrowing. Selection here has to
/// outlive the filter, because the point of it is to search, pick, search again, and act on the
/// union. So the list control keeps the one job the framework does well - focus, arrow keys,
/// scrolling - and what is <em>picked</em> is a property of the row.
/// </para>
/// <para>
/// <b>Shown and selected are different questions.</b> Everything here is counted twice: against the
/// rows, and against what the view is showing of them. That is what lets the bar say "47 selected,
/// 12 of them are not shown" rather than quietly acting on rows the user cannot see - and it is what
/// <see cref="DeselectHidden"/> is for.
/// </para>
/// </remarks>
public sealed partial class ModListSelection : ObservableObject, IListSelection
{
    private readonly Func<IEnumerable?> _shown;
    private readonly Func<IReadOnlyList<ISelectableRow>> _all;
    private readonly Action<IReadOnlyList<ISelectableRow>> _activate;
    private readonly string _verb;

    /// <summary>Where a shift-click measures from. Set by every gesture that is not a shift-click.</summary>
    private ISelectableRow? _anchor;

    /// <summary>Set while a gesture is writing many rows, so the recount happens once at the end.</summary>
    private bool _changing;

    /// <summary>Set while the recount writes <see cref="AllShownSelected"/>, which is user-settable.</summary>
    private bool _syncing;

    private bool? _allShownSelected = false;


    /// <param name="shown">The view, in the order it renders - a function because it is replaced on reload.</param>
    /// <param name="all">Every row, shown or not.</param>
    /// <param name="activate">What Enter and a double click do to the picked rows.</param>
    /// <param name="verb">What the primary button says it will do, e.g. "Add".</param>
    public ModListSelection(
        Func<IEnumerable?> shown,
        Func<IReadOnlyList<ISelectableRow>> all,
        Action<IReadOnlyList<ISelectableRow>> activate,
        string verb)
    {
        _shown = shown;
        _all = all;
        _activate = activate;
        _verb = verb;
    }


    /// <summary>Raised once per gesture, after the counts have settled.</summary>
    public event Action? Changed;


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectedText))]
    [NotifyPropertyChangedFor(nameof(ActionText))]
    private int _selectedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHidden))]
    [NotifyPropertyChangedFor(nameof(HiddenText))]
    [NotifyPropertyChangedFor(nameof(DeselectHiddenText))]
    private int _hiddenCount;


    /// <summary>
    /// The header's box. Null for a part-selected list, which is the state it spends most of its
    /// time in - and the one a two-state box would have to round to a lie.
    /// </summary>
    public bool? AllShownSelected
    {
        get => _allShownSelected;
        set
        {
            if (_syncing)
            {
                _allShownSelected = value;
                OnPropertyChanged();

                return;
            }

            // Never written to null by the user: the box is not three-state, so a click on a
            // part-selected list means "select the rest", which is the useful reading.
            if (value is true)
            {
                SelectAllShown();
            }
            else if (value is false)
            {
                DeselectAllShown();
            }
        }
    }

    public bool HasSelection => SelectedCount > 0;
    public bool HasHidden => HiddenCount > 0;

    public string SelectedText => SelectedCount == 1 ? "1 selected" : $"{SelectedCount} selected";

    public string ActionText => SelectedCount == 1 ? $"{_verb} 1 mod" : $"{_verb} {SelectedCount} mods";

    /// <summary>
    /// Worded as a fact about the list rather than as a warning. These rows are selected on purpose -
    /// picking a set across several searches is what the selection is for - and all the bar has to do
    /// is make sure the count on the button is never a surprise.
    /// </summary>
    public string HiddenText => HiddenCount == 1
        ? "1 of them is not shown"
        : $"{HiddenCount} of them are not shown";

    public string DeselectHiddenText => HiddenCount == 1 ? "Deselect 1 hidden" : $"Deselect {HiddenCount} hidden";


    public void Click(object? item)
    {
        if (item is not ISelectableRow row)
        {
            return;
        }

        InOneGesture(() =>
        {
            Clear();

            row.IsSelected = true;
            _anchor = row;
        });
    }

    public void Toggle(object? item)
    {
        if (item is not ISelectableRow row)
        {
            return;
        }

        InOneGesture(() =>
        {
            row.IsSelected = row.IsSelected is false;
            _anchor = row;
        });
    }

    public void ExtendTo(object? item)
    {
        if (item is not ISelectableRow row)
        {
            return;
        }

        if (_anchor is null || ReferenceEquals(_anchor, row))
        {
            Click(row);

            return;
        }

        var shown = Rows();
        var from = shown.IndexOf(_anchor);
        var to = shown.IndexOf(row);

        if (from < 0 || to < 0)
        {
            // The anchor has been filtered away since it was set. Re-anchoring here rather than
            // extending from a row nobody can see keeps the range to what is on screen.
            Click(row);

            return;
        }

        InOneGesture(() =>
        {
            Clear();

            for (var index = Math.Min(from, to); index <= Math.Max(from, to); index++)
            {
                shown[index].IsSelected = true;
            }

            _anchor = shown[from];
        });
    }

    public void EnsureSelected(object? item)
    {
        if (item is ISelectableRow row && row.IsSelected is false)
        {
            Click(row);
        }
    }

    public void SelectAllShown()
    {
        InOneGesture(() =>
        {
            foreach (var row in Rows())
            {
                row.IsSelected = true;
            }
        });
    }

    public void DeselectAllShown()
    {
        InOneGesture(() =>
        {
            foreach (var row in Rows())
            {
                row.IsSelected = false;
            }
        });
    }

    /// <summary>
    /// Drops everything the view is not showing, leaving the selection equal to what is on screen.
    /// The counterpart of a selection that survives the search: what makes carrying rows across
    /// searches safe is being able to put down the ones no longer in hand.
    /// </summary>
    public void DeselectHidden()
    {
        var shown = new HashSet<ISelectableRow>(Rows());

        InOneGesture(() =>
        {
            foreach (var row in _all())
            {
                if (shown.Contains(row) is false)
                {
                    row.IsSelected = false;
                }
            }
        });
    }

    public void ClearSelection()
    {
        InOneGesture(Clear);
    }

    public void Activate(object? item)
    {
        var picked = Picked();

        // A double click on a row outside the selection is about that row, and says so by replacing
        // the selection first - otherwise the same gesture would mean different things depending on
        // something off screen.
        if (item is ISelectableRow row && row.IsSelected is false)
        {
            Click(row);

            picked = [row];
        }

        if (picked.Count > 0)
        {
            _activate(picked);
        }
    }

    /// <summary>Every row the user has picked, shown or not, in list order.</summary>
    public IReadOnlyList<ISelectableRow> Picked() => [.. _all().Where(x => x.IsSelected)];

    /// <summary>
    /// Recounts from the rows. Called by the page whenever something other than a gesture could have
    /// moved a flag - a row's own checkbox, a reload, a filter change, a bulk move that emptied a
    /// list.
    /// </summary>
    public void Recount()
    {
        if (_changing)
        {
            return;
        }

        var shownCount = 0;
        var shownSelected = 0;

        foreach (var row in Rows())
        {
            shownCount++;

            if (row.IsSelected)
            {
                shownSelected++;
            }
        }

        var total = _all().Count(x => x.IsSelected);

        SelectedCount = total;
        HiddenCount = total - shownSelected;

        _syncing = true;

        try
        {
            AllShownSelected = shownCount > 0 && shownSelected == shownCount
                ? true
                : shownSelected == 0 ? false : null;
        }
        finally
        {
            _syncing = false;
        }

        Changed?.Invoke();
    }


    private void Clear()
    {
        foreach (var row in _all())
        {
            row.IsSelected = false;
        }

        _anchor = null;
    }

    /// <summary>
    /// One gesture, one recount. A shift-click across two thousand rows is two thousand property
    /// changes, and recounting on each of them would make the range the slowest thing on the page.
    /// </summary>
    private void InOneGesture(Action change)
    {
        _changing = true;

        try
        {
            change();
        }
        finally
        {
            _changing = false;
        }

        Recount();
    }

    private List<ISelectableRow> Rows()
        => _shown() is IEnumerable view ? [.. view.OfType<ISelectableRow>()] : [];
}
