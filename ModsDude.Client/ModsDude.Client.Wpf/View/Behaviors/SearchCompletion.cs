using ModsDude.Client.Core.Helpers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ModsDude.Client.Wpf.View.Behaviors;

/// <summary>
/// Ctrl+Space in a mod search box: a list of the attribute keys, or of one key's values, under the
/// word being typed - narrowing as typing goes on, the way an editor's completion list does.
/// </summary>
/// <remarks>
/// <para>
/// <b>The list never takes focus.</b> The caret stays in the box the whole time, so typing carries
/// on narrowing and the keys the list needs - Up, Down, Enter, Tab, Escape - are taken from the
/// box's own key handling while it is open, and only then.
/// </para>
/// <para>
/// <b>Reads the box, not the view model.</b> The box's binding pushes text through on a delay, so
/// anything driven off the bound property would complete a word that is a beat out of date.
/// </para>
/// <para>
/// <b>Choosing a key goes straight on to its values</b>, which is the whole of the second step
/// anybody wanted; choosing a value closes the list and moves past it, ready for the next word.
/// </para>
/// </remarks>
public static class SearchCompletion
{
    public static readonly DependencyProperty CompleterProperty = DependencyProperty.RegisterAttached(
        "Completer",
        typeof(ModSearchCompleter),
        typeof(SearchCompletion),
        new PropertyMetadata(null, OnCompleterChanged));

    private static readonly DependencyProperty _stateProperty = DependencyProperty.RegisterAttached(
        "State",
        typeof(State),
        typeof(SearchCompletion),
        new PropertyMetadata(null));


    public static ModSearchCompleter? GetCompleter(DependencyObject element)
        => (ModSearchCompleter?)element.GetValue(CompleterProperty);

    public static void SetCompleter(DependencyObject element, ModSearchCompleter? value)
        => element.SetValue(CompleterProperty, value);

    /// <summary>
    /// Whether the list is showing under this box - for a page's own key handling, which has to leave
    /// Escape and the arrows alone while it is.
    /// </summary>
    public static bool IsOpen(TextBox box)
        => box.GetValue(_stateProperty) is State { IsOpen: true };


    private static void OnCompleterChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox box && box.GetValue(_stateProperty) is null)
        {
            box.SetValue(_stateProperty, new State(box));
        }
    }


    private sealed class State
    {
        private readonly TextBox _box;
        private readonly Popup _popup;
        private readonly ListBox _list;

        private ModSearchCompletion? _completion;

        /// <summary>Set while this class is writing the box itself, so the write does not re-query.</summary>
        private bool _writing;


        public State(TextBox box)
        {
            _box = box;

            _list = new ListBox
            {
                Focusable = false,
                MaxHeight = 320,
                MinWidth = 220,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
            _list.PreviewMouseLeftButtonDown += OnListMouseDown;

            var border = new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
                Child = _list
            };
            border.SetResourceReference(Border.CornerRadiusProperty, "OverlayCornerRadius");
            border.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorSecondaryBrush");
            border.SetResourceReference(Border.BorderBrushProperty, "DividerStrokeColorDefaultBrush");

            _popup = new Popup
            {
                PlacementTarget = box,
                Placement = PlacementMode.Bottom,
                VerticalOffset = 4,
                StaysOpen = false,
                AllowsTransparency = true,
                Child = border
            };

            box.PreviewKeyDown += OnKeyDown;
            box.TextChanged += (_, _) => Requery();
            box.SelectionChanged += (_, _) => Requery();
            box.LostKeyboardFocus += (_, _) => Close();
            box.Unloaded += (_, _) => Close();
        }


        public bool IsOpen => _popup.IsOpen;


        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key is Key.Space && Keyboard.Modifiers is ModifierKeys.Control)
            {
                // Handled even with nothing to offer, or the box would take it as a typed space.
                e.Handled = true;
                Open();
                return;
            }

            if (IsOpen is false)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.Down:
                    Move(1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    Move(-1);
                    e.Handled = true;
                    break;
                case Key.PageDown:
                    Move(8);
                    e.Handled = true;
                    break;
                case Key.PageUp:
                    Move(-8);
                    e.Handled = true;
                    break;
                case Key.Enter or Key.Tab:
                    if (_list.SelectedItem is ListBoxItem { Tag: ModSearchCompletionItem item })
                    {
                        Accept(item);
                        e.Handled = true;
                    }
                    break;
                case Key.Escape:
                    Close();
                    e.Handled = true;
                    break;
            }
        }

        private void Open()
        {
            if (Query() is false)
            {
                Close();
                return;
            }

            _popup.IsOpen = true;
        }

        /// <summary>Follows typing and caret moves while open; closes once there is nothing left to offer.</summary>
        private void Requery()
        {
            if (_writing || IsOpen is false)
            {
                return;
            }

            if (Query() is false)
            {
                Close();
            }
        }

        private bool Query()
        {
            if (GetCompleter(_box) is not ModSearchCompleter completer
                || completer.Complete(_box.Text, _box.CaretIndex) is not ModSearchCompletion completion)
            {
                _completion = null;
                return false;
            }

            // The item somebody had moved to stays picked while it is still on offer, so narrowing
            // does not throw them back to the top of the list.
            var picked = (_list.SelectedItem as ListBoxItem)?.Tag as ModSearchCompletionItem;

            _completion = completion;
            _list.Items.Clear();

            foreach (var item in completion.Items)
            {
                _list.Items.Add(CreateRow(item));
            }

            var index = picked is null ? -1 : completion.Items.ToList().FindIndex(x => x.Text == picked.Text);
            _list.SelectedIndex = Math.Max(index, 0);
            _list.ScrollIntoView(_list.SelectedItem);

            // Under the start of the word being completed rather than the start of the box.
            // Empty before the box has been laid out, which is a left edge of infinity - so the start
            // of the box, then.
            var anchor = _box.GetRectFromCharacterIndex(Math.Min(completion.ReplaceStart, _box.Text.Length));
            _popup.PlacementRectangle = new Rect(anchor.IsEmpty ? 0 : anchor.Left, 0, 1, _box.ActualHeight);

            return true;
        }

        private static ListBoxItem CreateRow(ModSearchCompletionItem item)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock { Text = item.IsKey ? $"{item.Text}:" : item.Text });

            if (item.Hint is not null)
            {
                var hint = new TextBlock { Text = item.Hint, Margin = new Thickness(8, 0, 0, 0) };
                hint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
                panel.Children.Add(hint);
            }

            // Tighter than the theme's list rows: this is a list read at a glance while typing, and a
            // category list is a hundred and fifty long.
            return new ListBoxItem
            {
                Content = panel,
                Tag = item,
                Focusable = false,
                MinHeight = 0,
                Padding = new Thickness(10, 5, 10, 5)
            };
        }

        private void Move(int by)
        {
            if (_list.Items.Count == 0)
            {
                return;
            }

            _list.SelectedIndex = Math.Clamp(_list.SelectedIndex + by, 0, _list.Items.Count - 1);
            _list.ScrollIntoView(_list.SelectedItem);
        }

        private void OnListMouseDown(object sender, MouseButtonEventArgs e)
        {
            var element = e.OriginalSource as DependencyObject;

            while (element is not null and not ListBoxItem)
            {
                element = VisualTreeHelper.GetParent(element);
            }

            if (element is ListBoxItem { Tag: ModSearchCompletionItem item })
            {
                Accept(item);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Replaces the whole word the list was offered for. A key keeps the list open on its values;
        /// a value is followed by a space, so the next word can start straight away.
        /// </summary>
        private void Accept(ModSearchCompletionItem item)
        {
            if (_completion is not ModSearchCompletion completion)
            {
                return;
            }

            var text = _box.Text;
            var end = Math.Min(completion.ReplaceStart + completion.ReplaceLength, text.Length);
            var rest = text[end..];

            var insert = item.Insert;
            var caret = completion.ReplaceStart + insert.Length;

            if (item.IsKey is false)
            {
                if (rest.Length == 0 || char.IsWhiteSpace(rest[0]) is false)
                {
                    insert += " ";
                }

                caret = completion.ReplaceStart + insert.Length + (insert.EndsWith(' ') ? 0 : 1);
            }

            _writing = true;

            try
            {
                _box.Text = text[..completion.ReplaceStart] + insert + rest;
                _box.CaretIndex = Math.Min(caret, _box.Text.Length);
            }
            finally
            {
                _writing = false;
            }

            if (item.IsKey)
            {
                Open();
            }
            else
            {
                Close();
            }
        }

        private void Close()
        {
            _popup.IsOpen = false;
            _completion = null;
        }
    }
}
