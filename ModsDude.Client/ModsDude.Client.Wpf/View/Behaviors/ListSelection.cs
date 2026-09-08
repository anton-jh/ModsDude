using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace ModsDude.Client.Wpf.View.Behaviors;

/// <summary>
/// Gives a <see cref="ListBox"/> the selection gestures of a file manager - click, ctrl-click,
/// shift-click, arrow keys, Ctrl+A, space, Enter, double click - and routes every one of them to an
/// <see cref="IListSelection"/> instead of to the list's own selection.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not the list's own selection.</b> A <see cref="ListBox"/> in extended mode already does
/// most of this, but it drops from its selection anything the collection view filters out - so
/// typing one more character into the search box would throw away the set the user was assembling.
/// The editor's selection has to survive the search, so the flag lives on the row view models and
/// this class is the translation layer. See <see cref="ModListSelection"/>.
/// </para>
/// <para>
/// <b>The list keeps its current item.</b> <c>SelectionMode="Single"</c> is left switched on and
/// untouched, because "which row has focus" is a real and separate thing that the framework tracks
/// well: it is what the arrow keys move and what shift-click measures against. Its highlight is
/// rendered as a focus ring rather than as a selection, which is the same distinction Explorer draws
/// between the focus rectangle and the picked set.
/// </para>
/// <para>
/// <b>What a row's own controls keep.</b> A press that lands on a button, a checkbox, a combo box or
/// a link is that control's, not the list's - so the version selector, the lock toggle and the
/// per-row move buttons all still work on a row that is part of a large selection, and ticking a
/// row's checkbox adds it to the selection instead of replacing the selection with it.
/// </para>
/// </remarks>
public static class ListSelection
{
    public static readonly DependencyProperty ControllerProperty = DependencyProperty.RegisterAttached(
        "Controller",
        typeof(IListSelection),
        typeof(ListSelection),
        new PropertyMetadata(null, OnControllerChanged));

    /// <summary>
    /// A press on a row that is already picked, waiting to find out whether it was a click or the
    /// start of a drag. See <see cref="OnPreviewMouseLeftButtonDown"/>.
    /// </summary>
    private static readonly DependencyProperty _pendingProperty = DependencyProperty.RegisterAttached(
        "Pending",
        typeof(PendingClick),
        typeof(ListSelection),
        new PropertyMetadata(null));


    private sealed record PendingClick(Point Origin, object Item);


    public static IListSelection? GetController(DependencyObject element)
        => (IListSelection?)element.GetValue(ControllerProperty);

    public static void SetController(DependencyObject element, IListSelection? value)
        => element.SetValue(ControllerProperty, value);


    private static void OnControllerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox list)
        {
            return;
        }

        list.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        list.PreviewMouseMove -= OnPreviewMouseMove;
        list.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
        list.PreviewMouseRightButtonDown -= OnPreviewMouseRightButtonDown;
        list.MouseDoubleClick -= OnMouseDoubleClick;
        list.PreviewKeyDown -= OnPreviewKeyDown;

        if (e.NewValue is IListSelection)
        {
            list.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            list.PreviewMouseMove += OnPreviewMouseMove;
            list.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
            list.PreviewMouseRightButtonDown += OnPreviewMouseRightButtonDown;
            list.MouseDoubleClick += OnMouseDoubleClick;
            list.PreviewKeyDown += OnPreviewKeyDown;
        }
    }


    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var list = (ListBox)sender;

        // A drag runs its own message loop and swallows the release that ends it, so a press left
        // waiting from last time is cleared here rather than only on the way up.
        list.ClearValue(_pendingProperty);

        if (Resolve(list, e.OriginalSource) is not (IListSelection controller, object item))
        {
            return;
        }

        // Not handled: the list still gets to move its current item and take focus, which is what
        // makes the arrow keys carry on from wherever the pointer left off.
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            controller.ExtendTo(item);
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            controller.Toggle(item);
        }
        else if (item is ISelectableRow picked && picked.IsSelected)
        {
            // Pressing a row that is already picked must not collapse the selection onto it: that
            // press is how a drag of the whole selection begins. It becomes a plain click on the way
            // back up, if no drag happened in between - which is what Explorer does, and the only
            // arrangement in which "grab these forty and drag them" is possible at all.
            list.SetValue(_pendingProperty, new PendingClick(e.GetPosition(list), item));
        }
        else
        {
            controller.Click(item);
        }
    }

    private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        var list = (ListBox)sender;

        if (list.GetValue(_pendingProperty) is not PendingClick pending)
        {
            return;
        }

        if (e.LeftButton is not MouseButtonState.Pressed)
        {
            list.ClearValue(_pendingProperty);

            return;
        }

        var position = e.GetPosition(list);

        // Far enough to be a drag, so the press was never a click and the selection stands.
        if (Math.Abs(position.X - pending.Origin.X) >= SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(position.Y - pending.Origin.Y) >= SystemParameters.MinimumVerticalDragDistance)
        {
            list.ClearValue(_pendingProperty);
        }
    }

    private static void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var list = (ListBox)sender;

        if (list.GetValue(_pendingProperty) is PendingClick pending)
        {
            list.ClearValue(_pendingProperty);

            GetController(list)?.Click(pending.Item);
        }
    }

    private static void OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var list = (ListBox)sender;

        if (Resolve(list, e.OriginalSource) is (IListSelection controller, object item))
        {
            controller.EnsureSelected(item);
        }
    }

    private static void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var list = (ListBox)sender;

        if (Resolve(list, e.OriginalSource) is not (IListSelection controller, object item))
        {
            return;
        }

        controller.Activate(item);

        e.Handled = true;
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var list = (ListBox)sender;

        if (GetController(list) is not IListSelection controller)
        {
            return;
        }

        // A press inside a row's own text box or combo box is that control's business.
        if (e.OriginalSource is DependencyObject source && OwnsItsOwnInput(source, list))
        {
            return;
        }

        switch (e.Key)
        {
            case Key.A when Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                controller.SelectAllShown();
                break;

            case Key.Escape:
                controller.ClearSelection();
                break;

            case Key.Space:
                controller.Toggle(list.SelectedItem);
                break;

            case Key.Enter:
                controller.Activate(null);
                break;

            case Key.Up:
            case Key.Down:
                Move(list, controller, e.Key is Key.Down ? 1 : -1);
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>
    /// Moves the current item and takes the selection with it, or extends to it while shift is
    /// held - the arrow-key half of click and shift-click. Handled here rather than left to the
    /// list, so that both keys and mouse write to the same selection.
    /// </summary>
    private static void Move(ListBox list, IListSelection controller, int delta)
    {
        var items = list.Items;

        if (items.Count == 0)
        {
            return;
        }

        var current = list.SelectedItem is null ? -1 : items.IndexOf(list.SelectedItem);
        var next = Math.Clamp(current + delta, 0, items.Count - 1);

        if (current < 0)
        {
            // Arriving from the search box: start at the end the key is pointing at rather than
            // one step in from it.
            next = delta > 0 ? 0 : items.Count - 1;
        }

        var item = items[next];

        list.SelectedItem = item;
        list.ScrollIntoView(item);
        list.UpdateLayout();

        if (list.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container)
        {
            container.Focus();
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            controller.ExtendTo(item);
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) is false)
        {
            controller.Click(item);
        }
    }

    /// <summary>
    /// The controller and the row a gesture landed on, or null when it landed on nothing or on a
    /// control inside the row that answers clicks itself.
    /// </summary>
    private static (IListSelection Controller, object Item)? Resolve(ListBox list, object source)
    {
        if (GetController(list) is not IListSelection controller
            || source is not DependencyObject element
            || OwnsItsOwnInput(element, list)
            || FindContainer(element) is not ListBoxItem container
            || container.DataContext is not object item)
        {
            return null;
        }

        return (controller, item);
    }

    /// <summary>
    /// Whether anything between the press and the row is a control with a click of its own. Walked
    /// rather than tested on the original source alone, because the source is usually a piece of a
    /// template - the text inside a button, the border inside a checkbox.
    /// </summary>
    private static bool OwnsItsOwnInput(DependencyObject source, ListBox list)
    {
        DependencyObject? current = source;

        while (current is not null && current != list)
        {
            if (current is ButtonBase or ComboBox or ComboBoxItem or TextBoxBase or Hyperlink or Thumb)
            {
                return true;
            }

            if (current is ListBoxItem)
            {
                return false;
            }

            current = Parent(current);
        }

        return false;
    }

    private static ListBoxItem? FindContainer(DependencyObject source)
    {
        DependencyObject? current = source;

        while (current is not null)
        {
            if (current is ListBoxItem container)
            {
                return container;
            }

            current = Parent(current);
        }

        return null;
    }

    /// <summary>
    /// A hyperlink is a content element rather than a visual, so a walk that only knew about the
    /// visual tree would stop at the text block holding it.
    /// </summary>
    private static DependencyObject? Parent(DependencyObject current)
        => current is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(current)
            : LogicalTreeHelper.GetParent(current);
}
