using ModsDude.Server.Domain.Retention;

namespace ModsDude.Server.Domain.Tests.Retention;

public class RetentionPolicyTests
{
    private static readonly DateOnly _today = new(2026, 9, 23);
    private static readonly RetentionRule _rule = new(Window: 3, GraceDays: 30);


    [Fact]
    public void An_empty_history_has_nothing_to_schedule()
    {
        Assert.Empty(RetentionPolicy.Evaluate(History(), window: 3));
    }

    [Fact]
    public void A_history_of_one_keeps_it()
    {
        Assert.Empty(RetentionPolicy.Evaluate(History(1), window: 3));
    }

    [Fact]
    public void Rows_older_than_the_window_are_outside_it()
    {
        var eligible = RetentionPolicy.Evaluate(History(1, 2, 3, 4, 5), window: 3);

        Assert.Equal(
            [(1, DeletionReason.OutsideWindow), (2, DeletionReason.OutsideWindow)],
            eligible.OrderBy(x => x.Key).Select(x => (x.Key, x.Value)));
    }

    [Fact]
    public void Arrival_order_does_not_decide_what_is_recent()
    {
        var eligible = RetentionPolicy.Evaluate(History(4, 1, 5, 3, 2), window: 3);

        Assert.Equal([1, 2], eligible.Keys.Order());
    }

    /// <summary>
    /// The window only winds down once everything beyond it has gone. While older rows still exist -
    /// even scheduled ones - the newest few are what the history is keeping.
    /// </summary>
    [Fact]
    public void The_window_is_kept_while_anything_older_remains()
    {
        var eligible = RetentionPolicy.Evaluate(History(1, 2, 3, 4), window: 3);

        Assert.Equal([1], eligible.Keys);
        Assert.Equal(DeletionReason.OutsideWindow, eligible[1]);
    }

    [Fact]
    public void A_history_down_to_its_window_winds_down_to_its_newest_row()
    {
        var eligible = RetentionPolicy.Evaluate(History(7, 8, 9), window: 3);

        Assert.Equal(
            [(7, DeletionReason.WindingDown), (8, DeletionReason.WindingDown)],
            eligible.OrderBy(x => x.Key).Select(x => (x.Key, x.Value)));
    }

    /// <summary>
    /// "Three or fewer", not "exactly three": a history of two that never grew to three would
    /// otherwise keep both rows forever while one of three shrank to one.
    /// </summary>
    [Fact]
    public void A_history_shorter_than_its_window_winds_down_too()
    {
        var eligible = RetentionPolicy.Evaluate(History(8, 9), window: 3);

        Assert.Equal([8], eligible.Keys);
        Assert.Equal(DeletionReason.WindingDown, eligible[8]);
    }

    [Fact]
    public void A_window_of_two_winds_down_to_one()
    {
        var eligible = RetentionPolicy.Evaluate(History(1, 2), window: 2);

        Assert.Equal([1], eligible.Keys);
        Assert.Equal(DeletionReason.WindingDown, eligible[1]);
    }

    [Fact]
    public void A_held_row_is_never_scheduled()
    {
        var eligible = RetentionPolicy.Evaluate(
            [Row(1, held: true), Row(2), Row(3), Row(4), Row(5)],
            window: 3);

        Assert.Equal([2], eligible.Keys);
    }

    /// <summary>
    /// A history something still needs keeps its window: a profile with a savegame played on any of
    /// its revisions keeps its latest three, whichever revision the save was played on.
    /// </summary>
    [Fact]
    public void A_history_with_any_held_row_does_not_wind_down()
    {
        var eligible = RetentionPolicy.Evaluate(
            [Row(1, held: true), Row(2), Row(3)],
            window: 3);

        Assert.Empty(eligible);
    }

    [Fact]
    public void A_held_row_outside_the_window_does_not_stop_its_neighbours_going()
    {
        var eligible = RetentionPolicy.Evaluate(
            [Row(1), Row(2, held: true), Row(3), Row(4), Row(5), Row(6)],
            window: 3);

        Assert.Equal([1, 3], eligible.Keys.Order());
    }

    [Fact]
    public void The_newest_row_is_never_scheduled_even_when_nothing_holds_anything()
    {
        var eligible = RetentionPolicy.Evaluate(History(1, 2, 3), window: 1);

        Assert.DoesNotContain(3, eligible.Keys);
    }

    [Fact]
    public void A_window_below_one_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RetentionPolicy.Evaluate(History(1, 2), window: 0));
    }


    [Fact]
    public void A_newly_eligible_row_is_scheduled_its_grace_period_from_today()
    {
        var changes = RetentionPolicy.Reconcile(
            Eligible((1, DeletionReason.OutsideWindow)),
            Scheduled(),
            _rule,
            _today);

        Assert.Equal(new DeletionSchedule(_today.AddDays(30), DeletionReason.OutsideWindow), changes.Schedule[1]);
        Assert.Empty(changes.Unschedule);
    }

    [Fact]
    public void A_row_still_eligible_for_the_same_reason_keeps_its_date()
    {
        var changes = RetentionPolicy.Reconcile(
            Eligible((1, DeletionReason.OutsideWindow)),
            Scheduled((1, new DeletionSchedule(_today.AddDays(3), DeletionReason.OutsideWindow))),
            _rule,
            _today);

        Assert.True(changes.IsEmpty);
    }

    /// <summary>
    /// Winding down stopped applying - a new row arrived - and the row fell outside the window in the
    /// same move. It is a different decision, so it gets its own full grace period.
    /// </summary>
    [Fact]
    public void A_row_eligible_for_another_reason_is_rescheduled_from_today()
    {
        var changes = RetentionPolicy.Reconcile(
            Eligible((1, DeletionReason.OutsideWindow)),
            Scheduled((1, new DeletionSchedule(_today.AddDays(3), DeletionReason.WindingDown))),
            _rule,
            _today);

        Assert.Equal(new DeletionSchedule(_today.AddDays(30), DeletionReason.OutsideWindow), changes.Schedule[1]);
    }

    [Fact]
    public void A_row_no_longer_eligible_is_unscheduled()
    {
        var changes = RetentionPolicy.Reconcile(
            Eligible(),
            Scheduled((1, new DeletionSchedule(_today.AddDays(3), DeletionReason.WindingDown))),
            _rule,
            _today);

        Assert.Empty(changes.Schedule);
        Assert.Equal([1], changes.Unschedule);
    }


    [Fact]
    public void A_schedule_is_stale_when_its_row_stopped_being_eligible_or_changed_reason()
    {
        var stale = RetentionPolicy.FindStale(
            Eligible((1, DeletionReason.OutsideWindow), (2, DeletionReason.OutsideWindow)),
            Scheduled(
                (1, new DeletionSchedule(_today, DeletionReason.OutsideWindow)),
                (2, new DeletionSchedule(_today, DeletionReason.WindingDown)),
                (3, new DeletionSchedule(_today, DeletionReason.WindingDown))));

        Assert.Equal([2, 3], stale.Order());
    }


    [Fact]
    public void Due_rows_are_those_scheduled_for_today_or_earlier()
    {
        var due = RetentionPolicy.FindDue(
            Eligible((1, DeletionReason.OutsideWindow), (2, DeletionReason.OutsideWindow), (3, DeletionReason.OutsideWindow)),
            Scheduled(
                (1, new DeletionSchedule(_today.AddDays(-1), DeletionReason.OutsideWindow)),
                (2, new DeletionSchedule(_today, DeletionReason.OutsideWindow)),
                (3, new DeletionSchedule(_today.AddDays(1), DeletionReason.OutsideWindow))),
            _today);

        Assert.Equal([1, 2], due.Order());
    }

    /// <summary>
    /// A schedule is cleared eventually, not instantly, and a deletion cannot be taken back. So the
    /// date alone never decides: the row has to be eligible now, for the reason it was scheduled for.
    /// </summary>
    [Fact]
    public void A_due_row_that_is_no_longer_eligible_for_its_reason_is_not_deleted()
    {
        var due = RetentionPolicy.FindDue(
            Eligible((2, DeletionReason.OutsideWindow)),
            Scheduled(
                (1, new DeletionSchedule(_today, DeletionReason.OutsideWindow)),
                (2, new DeletionSchedule(_today, DeletionReason.WindingDown))),
            _today);

        Assert.Empty(due);
    }


    private static RetentionCandidate<int> Row(int number, bool held = false) => new(number, number, held);

    private static IReadOnlyList<RetentionCandidate<int>> History(params int[] numbers)
        => [.. numbers.Select(x => Row(x))];

    private static Dictionary<int, DeletionReason> Eligible(params (int Key, DeletionReason Reason)[] rows)
        => rows.ToDictionary(x => x.Key, x => x.Reason);

    private static Dictionary<int, DeletionSchedule> Scheduled(params (int Key, DeletionSchedule Schedule)[] rows)
        => rows.ToDictionary(x => x.Key, x => x.Schedule);
}
