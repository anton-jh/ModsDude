using System.ComponentModel;

namespace ModsDude.Client.Wpf.Shared.Behaviors;

/// <summary>
/// What a list's mouse and keyboard gestures do to a selection. The view talks to this and to
/// nothing else, so the same behaviour drives every list that uses <see cref="ListSelection"/>.
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

/// <summary>A row that can be picked out of a list, whichever kind of row it is.</summary>
public interface ISelectableRow : INotifyPropertyChanged
{
    bool IsSelected { get; set; }
}
