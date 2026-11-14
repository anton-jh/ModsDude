using ModsDude.Client.Core.Helpers;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Core.Tests.Helpers;

public class ObservableCollectionSynchronizerTests
{
    [Fact]
    public void Added_items_appear_sorted()
    {
        var harness = new Harness();

        harness.Source.Add("b");
        harness.Source.Add("a");

        Assert.Equal(["a", "b"], harness.Names);
    }

    [Fact]
    public void A_moved_item_keeps_its_one_view_model()
    {
        var harness = new Harness("a", "b");
        var before = harness.Target.ToList();

        harness.Source.Move(0, 1);

        Assert.Equal(before, harness.Target);
        Assert.All(before, x => Assert.False(x.IsDisposed));

        harness.Source.Remove("a");

        Assert.Equal(["b"], harness.Names);
    }

    [Fact]
    public void A_replaced_item_gets_a_new_view_model_and_the_old_one_is_released()
    {
        var harness = new Harness("a", "b");
        var old = harness.Target.Single(x => x.Name == "a");

        harness.Source[0] = "c";

        Assert.Equal(["b", "c"], harness.Names);
        Assert.True(old.IsDisposed);
    }

    [Fact]
    public void An_item_replaced_by_itself_is_still_shown_once()
    {
        var harness = new Harness("a", "b");

        harness.Source[0] = "a";

        Assert.Equal(["a", "b"], harness.Names);
        Assert.Single(harness.Target, x => x.Name == "a" && x.IsDisposed is false);
    }

    [Fact]
    public void A_removed_item_is_released()
    {
        var harness = new Harness("a", "b");
        var removed = harness.Target.Single(x => x.Name == "a");

        harness.Source.Remove("a");

        Assert.Equal(["b"], harness.Names);
        Assert.True(removed.IsDisposed);
    }

    [Fact]
    public void Clearing_releases_everything()
    {
        var harness = new Harness("a", "b");
        var before = harness.Target.ToList();

        harness.Source.Clear();

        Assert.Empty(harness.Target);
        Assert.All(before, x => Assert.True(x.IsDisposed));
    }


    private sealed class Harness
    {
        public Harness(params string[] items)
        {
            foreach (var item in items)
            {
                Source.Add(item);
            }

            Synchronizer = new(Source, Target, x => new Item(x), x => x.Name, StringComparer.Ordinal);
        }

        public ObservableCollection<string> Source { get; } = [];
        public ObservableCollection<Item> Target { get; } = [];
        public ObservableCollectionSynchronizer<string, Item, string> Synchronizer { get; }

        public List<string> Names => [.. Target.Select(x => x.Name)];
    }

    private sealed class Item(string name) : IDisposable
    {
        public string Name { get; } = name;
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
