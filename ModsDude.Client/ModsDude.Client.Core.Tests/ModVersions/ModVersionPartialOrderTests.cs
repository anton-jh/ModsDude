using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModVersions;
using static ModsDude.Client.Core.Tests.Keys;

namespace ModsDude.Client.Core.Tests.ModVersions;

public class ModVersionPartialOrderTests
{
    [Fact]
    public void A_fully_ordered_set_comes_back_in_order()
    {
        var ordering = ModVersionPartialOrder.Derive(
            Vs("1.2.0.0", "1.0.0.0", "1.10.0.0", "1.9.0.0"),
            DefaultModVersionComparer.Instance);

        Assert.Equal(Vs("1.0.0.0", "1.2.0.0", "1.9.0.0", "1.10.0.0"), ordering.Order);
        Assert.Empty(ordering.UnorderedPairs);
        Assert.True(ordering.IsFullyOrdered);
    }

    [Fact]
    public void An_abstention_another_version_settles_transitively_is_not_a_question()
    {
        var comparer = new ScriptedComparer(["a", "b", "c"], abstainOn: [("a", "c")]);

        var ordering = ModVersionPartialOrder.Derive(Vs("c", "a", "b"), comparer);

        Assert.Equal(Vs("a", "b", "c"), ordering.Order);
        Assert.Empty(ordering.UnorderedPairs);
    }

    [Fact]
    public void An_abstention_nothing_settles_is_reported_as_a_question()
    {
        var comparer = new ScriptedComparer(["a", "b", "c"], abstainOn: [("b", "c")]);

        var ordering = ModVersionPartialOrder.Derive(Vs("a", "b", "c"), comparer);

        Assert.Equal(new ModVersionPair(V("b"), V("c")), Assert.Single(ordering.UnorderedPairs));
        Assert.False(ordering.IsFullyOrdered);
    }

    [Fact]
    public void A_version_the_comparer_cannot_place_against_anything_is_a_question_against_each_of_them()
    {
        var comparer = new ScriptedComparer(["a", "b", "c"], abstainOn: [("a", "c"), ("b", "c")]);

        var ordering = ModVersionPartialOrder.Derive(Vs("a", "b", "c"), comparer);

        Assert.Equal(
            [new ModVersionPair(V("a"), V("c")), new ModVersionPair(V("b"), V("c"))],
            ordering.UnorderedPairs);
    }

    [Fact]
    public void An_unordered_pair_still_leaves_the_versions_in_the_derived_order()
    {
        var comparer = new ScriptedComparer(["a", "b", "c"], abstainOn: [("a", "b")]);

        var ordering = ModVersionPartialOrder.Derive(Vs("b", "a", "c"), comparer);

        // Neither of a and b is placed against the other, so the input order breaks the tie - and
        // both still precede c, which is settled.
        Assert.Equal(Vs("b", "a", "c"), ordering.Order);
    }

    [Fact]
    public void An_under_determined_order_comes_out_the_same_on_every_run()
    {
        var input = Vs("d", "b", "a", "c");
        var comparer = new ScriptedComparer(["a", "b", "c", "d"], abstainOn: [("a", "b"), ("c", "d")]);

        var first = ModVersionPartialOrder.Derive(input, comparer);
        var second = ModVersionPartialOrder.Derive(input, comparer);
        var third = ModVersionPartialOrder.Derive(input, comparer);

        // b and a are interchangeable here, and so are d and c; the input order settles both.
        Assert.Equal(Vs("b", "a", "d", "c"), first.Order);
        Assert.Equal(
            [new ModVersionPair(V("b"), V("a")), new ModVersionPair(V("d"), V("c"))],
            first.UnorderedPairs);
        Assert.Equal(first.Order, second.Order);
        Assert.Equal(first.Order, third.Order);
        Assert.Equal(first.UnorderedPairs, third.UnorderedPairs);
    }

    [Fact]
    public void A_pair_is_reported_once_and_in_the_order_the_derivation_left_it()
    {
        var comparer = new ScriptedComparer(["a", "b"], abstainOn: [("a", "b")]);

        var ordering = ModVersionPartialOrder.Derive(Vs("b", "a"), comparer);

        Assert.Equal(new ModVersionPair(V("b"), V("a")), Assert.Single(ordering.UnorderedPairs));
    }

    [Fact]
    public void A_repeated_version_is_only_ordered_once()
    {
        var ordering = ModVersionPartialOrder.Derive(
            Vs("1.0", "1.1", "1.0"),
            DefaultModVersionComparer.Instance);

        Assert.Equal(Vs("1.0", "1.1"), ordering.Order);
    }


    [Fact]
    public void An_established_order_is_taken_as_fact_rather_than_asked_about_again()
    {
        var comparer = new ScriptedComparer([], abstainOn: []);

        var ordering = ModVersionPartialOrder.Derive(Vs("v1", "1.0", "1.00"), comparer, settledOrder: Vs("v1", "1.0", "1.00"));

        Assert.Equal(Vs("v1", "1.0", "1.00"), ordering.Order);
        Assert.Empty(ordering.UnorderedPairs);
    }

    [Fact]
    public void An_established_order_beats_a_comparer_that_disagrees_with_it()
    {
        var comparer = new ScriptedComparer(["c", "b", "a"], abstainOn: []);

        var ordering = ModVersionPartialOrder.Derive(Vs("a", "b", "c"), comparer, settledOrder: Vs("a", "b", "c"));

        Assert.Equal(Vs("a", "b", "c"), ordering.Order);
        Assert.Empty(ordering.UnorderedPairs);
    }

    [Fact]
    public void An_incoming_version_is_still_compared_against_the_established_ones()
    {
        var comparer = new ScriptedComparer(["a", "x", "b"], abstainOn: []);

        var ordering = ModVersionPartialOrder.Derive(Vs("a", "b", "x"), comparer, settledOrder: Vs("a", "b"));

        Assert.Equal(Vs("a", "x", "b"), ordering.Order);
    }

    [Fact]
    public void An_established_version_missing_from_the_set_is_ignored()
    {
        var comparer = new ScriptedComparer(["a", "b"], abstainOn: []);

        var ordering = ModVersionPartialOrder.Derive(Vs("a", "b"), comparer, settledOrder: Vs("a", "gone", "b"));

        Assert.Equal(Vs("a", "b"), ordering.Order);
    }


    [Fact]
    public void A_comparer_that_contradicts_itself_costs_questions_rather_than_an_order()
    {
        // a < b < c < a. Nothing here is trustworthy, so all three pairs go to the user instead of
        // the topological sort picking a winner out of the cycle.
        var comparer = new CyclicComparer();

        var ordering = ModVersionPartialOrder.Derive(Vs("a", "b", "c"), comparer);

        Assert.Equal(Vs("a", "b", "c"), ordering.Order);
        Assert.Equal(
            [
                new ModVersionPair(V("a"), V("b")),
                new ModVersionPair(V("a"), V("c")),
                new ModVersionPair(V("b"), V("c"))
            ],
            ordering.UnorderedPairs);
    }

    [Fact]
    public void A_contradiction_does_not_spread_to_versions_outside_it()
    {
        var comparer = new CyclicComparer();

        var ordering = ModVersionPartialOrder.Derive(Vs("a", "b", "c", "z"), comparer);

        Assert.Equal(V("z"), ordering.Order[3]);
        Assert.DoesNotContain(ordering.UnorderedPairs, x => x.First == V("z") || x.Second == V("z"));
    }

    [Fact]
    public void Versions_the_comparer_calls_equal_are_neither_ordered_nor_asked_about()
    {
        var comparer = new ScriptedComparer(["a", "b"], abstainOn: [], equalOn: [("a", "b")]);

        var ordering = ModVersionPartialOrder.Derive(Vs("a", "b"), comparer);

        Assert.Equal(Vs("a", "b"), ordering.Order);
        Assert.Empty(ordering.UnorderedPairs);
    }


    /// <summary>
    /// Orders by position in <paramref name="truth"/>, except for the pairs it is told to abstain
    /// or report equal on. Lets the ordering tests state exactly which comparisons are available
    /// without going looking for version strings that happen to produce them.
    /// </summary>
    private sealed class ScriptedComparer(
        IReadOnlyList<string> truth,
        (string, string)[] abstainOn,
        (string, string)[]? equalOn = null)
        : IModVersionComparer
    {
        public ModVersionComparison Compare(ModVersionKey left, ModVersionKey right)
        {
            if (Mentions(abstainOn, left, right))
            {
                return ModVersionComparison.Undecidable;
            }

            if (Mentions(equalOn ?? [], left, right))
            {
                return ModVersionComparison.Equal;
            }

            var leftPosition = PositionOf(left);
            var rightPosition = PositionOf(right);

            if (leftPosition < 0 || rightPosition < 0)
            {
                return ModVersionComparison.Undecidable;
            }

            return leftPosition < rightPosition
                ? ModVersionComparison.Earlier
                : ModVersionComparison.Later;
        }

        private static bool Mentions((string, string)[] pairs, ModVersionKey left, ModVersionKey right) =>
            pairs.Any(x => (x.Item1 == left.Value && x.Item2 == right.Value) || (x.Item1 == right.Value && x.Item2 == left.Value));

        private int PositionOf(ModVersionKey version)
        {
            for (var position = 0; position < truth.Count; position++)
            {
                if (truth[position] == version.Value)
                {
                    return position;
                }
            }

            return -1;
        }
    }

    private sealed class CyclicComparer : IModVersionComparer
    {
        private static readonly (string, string)[] _edges =
            [("a", "b"), ("b", "c"), ("c", "a"), ("a", "z"), ("b", "z"), ("c", "z")];

        public ModVersionComparison Compare(ModVersionKey left, ModVersionKey right)
        {
            if (_edges.Any(x => x.Item1 == left.Value && x.Item2 == right.Value))
            {
                return ModVersionComparison.Earlier;
            }

            if (_edges.Any(x => x.Item1 == right.Value && x.Item2 == left.Value))
            {
                return ModVersionComparison.Later;
            }

            return ModVersionComparison.Undecidable;
        }
    }
}
