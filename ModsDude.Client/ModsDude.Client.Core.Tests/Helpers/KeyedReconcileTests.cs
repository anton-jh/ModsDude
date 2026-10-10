using ModsDude.Client.Core.Helpers;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace ModsDude.Client.Core.Tests.Helpers;

public class KeyedReconcileTests
{
    private sealed class Row(string key, int value)
    {
        public string Key { get; } = key;

        public int Value { get; set; } = value;
    }


    [Fact]
    public void An_empty_list_takes_every_value_in_order()
    {
        var rows = new ObservableCollection<Row>();

        Reconcile(rows, ("a", 1), ("b", 2), ("c", 3));

        Assert.Equal(["a", "b", "c"], rows.Select(x => x.Key));
        Assert.Equal([1, 2, 3], rows.Select(x => x.Value));
    }

    [Fact]
    public void A_key_that_stays_keeps_its_row_and_takes_the_new_value()
    {
        var rows = new ObservableCollection<Row>();
        Reconcile(rows, ("a", 1), ("b", 2));
        var kept = rows[1];

        Reconcile(rows, ("a", 1), ("b", 20));

        Assert.Same(kept, rows[1]);
        Assert.Equal(20, rows[1].Value);
    }

    [Fact]
    public void A_key_that_goes_is_removed()
    {
        var rows = new ObservableCollection<Row>();
        Reconcile(rows, ("a", 1), ("b", 2), ("c", 3));

        Reconcile(rows, ("a", 1), ("c", 3));

        Assert.Equal(["a", "c"], rows.Select(x => x.Key));
    }

    [Fact]
    public void A_reordering_moves_the_rows_rather_than_replacing_them()
    {
        var rows = new ObservableCollection<Row>();
        Reconcile(rows, ("a", 1), ("b", 2), ("c", 3));
        var before = rows.ToDictionary(x => x.Key);
        var actions = new List<NotifyCollectionChangedAction>();
        rows.CollectionChanged += (_, e) => actions.Add(e.Action);

        Reconcile(rows, ("c", 3), ("a", 1), ("b", 2));

        Assert.Equal(["c", "a", "b"], rows.Select(x => x.Key));
        Assert.All(rows, x => Assert.Same(before[x.Key], x));
        Assert.All(actions, x => Assert.Equal(NotifyCollectionChangedAction.Move, x));
    }

    [Fact]
    public void Adding_removing_and_moving_at_once_ends_in_the_values_order()
    {
        var rows = new ObservableCollection<Row>();
        Reconcile(rows, ("a", 1), ("b", 2), ("c", 3), ("d", 4));

        Reconcile(rows, ("d", 4), ("e", 5), ("b", 2));

        Assert.Equal(["d", "e", "b"], rows.Select(x => x.Key));
    }

    [Fact]
    public void Running_it_again_with_the_same_values_changes_nothing()
    {
        var rows = new ObservableCollection<Row>();
        Reconcile(rows, ("a", 1), ("b", 2));
        var changes = 0;
        rows.CollectionChanged += (_, _) => changes++;

        Reconcile(rows, ("a", 1), ("b", 2));

        Assert.Equal(0, changes);
    }

    [Fact]
    public void No_values_empties_the_list()
    {
        var rows = new ObservableCollection<Row>();
        Reconcile(rows, ("a", 1), ("b", 2));

        Reconcile(rows);

        Assert.Empty(rows);
    }


    private static void Reconcile(ObservableCollection<Row> rows, params (string Key, int Value)[] values)
        => KeyedReconcile.Apply(
            rows,
            values,
            x => x.Key,
            x => x.Key,
            x => new Row(x.Key, x.Value),
            (row, value) => row.Value = value.Value);
}
