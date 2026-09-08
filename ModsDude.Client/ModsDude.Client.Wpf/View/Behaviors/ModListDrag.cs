using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace ModsDude.Client.Wpf.View.Behaviors;

/// <summary>
/// Drags the selection from one of the mod list editor's two lists to the other. Each list declares
/// which side it is with <see cref="KindProperty"/> and what a drop onto it should run with
/// <see cref="DropCommandProperty"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The payload is the side, not the rows.</b> What is being moved is whatever that side has
/// selected, which the view model already knows - so the drag carries only the name of the list it
/// started in, and the drop runs the target's own command. That also makes the one rule this needs
/// trivial to state: a drop is valid exactly when the two sides differ.
/// </para>
/// <para>
/// <b>A drag is a second way to do something, never the only way.</b> Every move here is also a
/// button and a keystroke, because a two-pane drag is undiscoverable, awkward with a long list, and
/// impossible without a pointer. It is here because with the panes side by side it is the gesture
/// people reach for first.
/// </para>
/// </remarks>
public static class ModListDrag
{
    private const string _format = "ModsDude.ModList.Kind";

    public static readonly DependencyProperty KindProperty = DependencyProperty.RegisterAttached(
        "Kind",
        typeof(string),
        typeof(ModListDrag),
        new PropertyMetadata(null, OnKindChanged));

    public static readonly DependencyProperty DropCommandProperty = DependencyProperty.RegisterAttached(
        "DropCommand",
        typeof(ICommand),
        typeof(ModListDrag),
        new PropertyMetadata(null));

    /// <summary>
    /// Whether a valid drag is currently over this list. Read by the surrounding border, so the drop
    /// target is the box the rows land in rather than a line between them - nothing here is ordered,
    /// so there is no position for an insertion caret to mean.
    /// </summary>
    public static readonly DependencyProperty IsOverProperty = DependencyProperty.RegisterAttached(
        "IsOver",
        typeof(bool),
        typeof(ModListDrag),
        new PropertyMetadata(false));

    private static readonly DependencyProperty _originProperty = DependencyProperty.RegisterAttached(
        "Origin",
        typeof(Point?),
        typeof(ModListDrag),
        new PropertyMetadata(null));


    public static string? GetKind(DependencyObject element) => (string?)element.GetValue(KindProperty);
    public static void SetKind(DependencyObject element, string? value) => element.SetValue(KindProperty, value);

    public static ICommand? GetDropCommand(DependencyObject element) => (ICommand?)element.GetValue(DropCommandProperty);
    public static void SetDropCommand(DependencyObject element, ICommand? value) => element.SetValue(DropCommandProperty, value);

    public static bool GetIsOver(DependencyObject element) => (bool)element.GetValue(IsOverProperty);
    public static void SetIsOver(DependencyObject element, bool value) => element.SetValue(IsOverProperty, value);


    private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox list)
        {
            return;
        }

        list.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        list.PreviewMouseMove -= OnPreviewMouseMove;
        list.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
        list.DragOver -= OnDragOver;
        list.DragLeave -= OnDragLeave;
        list.Drop -= OnDrop;

        if (e.NewValue is string)
        {
            list.AllowDrop = true;

            list.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            list.PreviewMouseMove += OnPreviewMouseMove;
            list.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
            list.DragOver += OnDragOver;
            list.DragLeave += OnDragLeave;
            list.Drop += OnDrop;
        }
    }


    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var list = (ListBox)sender;

        // Only presses that started on a row and not on one of its own controls can begin a drag.
        // A press on the scrollbar is the scrollbar's, and stealing it would break the thumb.
        var onRow = e.OriginalSource is DependencyObject source
            && OwnsItsOwnInput(source, list) is false
            && FindContainer(source) is not null;

        list.SetValue(_originProperty, onRow ? e.GetPosition(list) : null);
    }

    private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        var list = (ListBox)sender;

        if (e.LeftButton is not MouseButtonState.Pressed)
        {
            list.SetValue(_originProperty, null);

            return;
        }

        if (list.GetValue(_originProperty) is not Point origin || GetKind(list) is not string kind)
        {
            return;
        }

        var position = e.GetPosition(list);

        if (Math.Abs(position.X - origin.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - origin.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        // Cleared before the drag rather than after it: DoDragDrop runs its own message loop and
        // swallows the mouse-up that would otherwise clear this.
        list.SetValue(_originProperty, null);

        DragDrop.DoDragDrop(list, new DataObject(_format, kind), DragDropEffects.Move);
    }

    private static void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ((ListBox)sender).SetValue(_originProperty, null);
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        var list = (ListBox)sender;
        var accepted = Accepts(list, e.Data);

        SetIsOver(list, accepted);

        e.Effects = accepted ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private static void OnDragLeave(object sender, DragEventArgs e)
    {
        SetIsOver((ListBox)sender, false);
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        var list = (ListBox)sender;

        SetIsOver(list, false);

        if (Accepts(list, e.Data) is false)
        {
            return;
        }

        e.Handled = true;

        if (GetDropCommand(list) is ICommand command && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    /// <summary>A drop is valid exactly when it came from the other list.</summary>
    private static bool Accepts(ListBox list, IDataObject data)
        => data.GetDataPresent(_format)
        && data.GetData(_format) is string kind
        && GetKind(list) is string own
        && kind != own;

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

    private static DependencyObject? Parent(DependencyObject current)
        => current is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(current)
            : LogicalTreeHelper.GetParent(current);
}
