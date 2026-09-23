namespace ModsDude.Server.Domain.Retention;

/// <summary>
/// Why a row is scheduled for deletion. Recorded with the date, because a schedule only stands while
/// the reason it was made for still holds - see <see cref="RetentionPolicy.Reconcile{TKey}"/>.
/// </summary>
public enum DeletionReason
{
    /// <summary>
    /// Older than the most recent few, and nothing holds it. The ordinary way a row goes: newer ones
    /// have replaced it.
    /// </summary>
    OutsideWindow,

    /// <summary>
    /// One of the most recent few, in a history that has already shrunk to them and that nothing
    /// holds. The second step of a history nobody is using any more winding down to its latest row.
    /// </summary>
    WindingDown
}


/// <summary>When a row goes, and why. A date only: the deletion job's own time of day is the time.</summary>
public readonly record struct DeletionSchedule(DateOnly Date, DeletionReason Reason);


/// <summary>How many rows a history keeps by recency, and how long a scheduled one waits.</summary>
public readonly record struct RetentionRule(int Window, int GraceDays);


/// <summary>
/// One row of a history as the policy sees it.
/// </summary>
/// <param name="Order">Higher is newer. Unique within the history.</param>
/// <param name="IsHeld">
/// Whether something outside the history needs this row - a savegame snapshot played on a profile
/// revision, a profile revision pinning a mod version. A held row is never scheduled, and a history
/// with any held row never winds down.
/// </param>
public readonly record struct RetentionCandidate<TKey>(TKey Key, long Order, bool IsHeld);


/// <summary>What a reconciliation changes: rows to (re)schedule, and rows whose schedule no longer stands.</summary>
public record RetentionChanges<TKey>(
    IReadOnlyDictionary<TKey, DeletionSchedule> Schedule,
    IReadOnlyList<TKey> Unschedule)
    where TKey : notnull
{
    public bool IsEmpty => Schedule.Count == 0 && Unschedule.Count == 0;
}


/// <summary>
/// Decides which rows of one history - a savegame's snapshots, a profile's revisions, a mod's
/// versions - are due to go, and keeps the schedules recorded on them honest. Pure, for the reason
/// <see cref="Mods.BlobReclamation"/> is: this is the part that can destroy somebody's data, and it
/// is the part that can be tested without a database.
/// </summary>
/// <remarks>
/// <para>
/// A row is eligible for one of two reasons. <see cref="DeletionReason.OutsideWindow"/>: it is not
/// among the <see cref="RetentionRule.Window"/> newest, and nothing holds it.
/// <see cref="DeletionReason.WindingDown"/>: the history is down to the window or fewer rows, nothing
/// holds any of them, and it is not the newest. So an idle history shrinks in two steps - first what
/// newer rows replaced, then the window itself - and always keeps its newest row.
/// </para>
/// <para>
/// <b>A schedule stands only while its own reason does.</b> A row scheduled as winding down that
/// becomes eligible as outside the window instead is not the same decision with a new label: it is
/// rescheduled from today, so every row gets its full grace period under the rule that actually
/// applies to it.
/// </para>
/// </remarks>
public static class RetentionPolicy
{
    public static RetentionRule SavegameSnapshots { get; } = new(Window: 3, GraceDays: 30);
    public static RetentionRule ProfileRevisions { get; } = new(Window: 3, GraceDays: 14);
    public static RetentionRule ModVersions { get; } = new(Window: 2, GraceDays: 14);


    /// <returns>Every row that may be scheduled, and why. The newest row is never among them.</returns>
    public static IReadOnlyDictionary<TKey, DeletionReason> Evaluate<TKey>(
        IEnumerable<RetentionCandidate<TKey>> history,
        int window)
        where TKey : notnull
    {
        if (window < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(window), window, "A history cannot keep fewer than one row.");
        }

        var newestFirst = history.OrderByDescending(x => x.Order).ToList();
        var windingDown = newestFirst.Count <= window && !newestFirst.Any(x => x.IsHeld);

        var eligible = new Dictionary<TKey, DeletionReason>();

        // From the second row: the newest is what the history currently is, and never goes.
        for (var index = 1; index < newestFirst.Count; index++)
        {
            var candidate = newestFirst[index];

            if (candidate.IsHeld)
            {
                continue;
            }

            if (index >= window)
            {
                eligible[candidate.Key] = DeletionReason.OutsideWindow;
            }
            else if (windingDown)
            {
                eligible[candidate.Key] = DeletionReason.WindingDown;
            }
        }

        return eligible;
    }

    /// <summary>
    /// What the scheduling job writes: a schedule for every newly eligible row, a fresh one for every
    /// row whose reason changed, and none for a row that is no longer eligible. A row still eligible
    /// for the reason it was scheduled for keeps its date.
    /// </summary>
    public static RetentionChanges<TKey> Reconcile<TKey>(
        IReadOnlyDictionary<TKey, DeletionReason> eligible,
        IReadOnlyDictionary<TKey, DeletionSchedule> scheduled,
        RetentionRule rule,
        DateOnly today)
        where TKey : notnull
    {
        var schedule = new Dictionary<TKey, DeletionSchedule>();

        foreach (var (key, reason) in eligible)
        {
            if (scheduled.TryGetValue(key, out var existing) && existing.Reason == reason)
            {
                continue;
            }

            schedule[key] = new DeletionSchedule(today.AddDays(rule.GraceDays), reason);
        }

        var unschedule = scheduled.Keys
            .Where(x => !eligible.ContainsKey(x))
            .ToList();

        return new RetentionChanges<TKey>(schedule, unschedule);
    }

    /// <summary>
    /// The schedules that no longer stand - the row stopped being eligible, or is eligible for another
    /// reason. What a write clears straight away, so that a row stops showing a deletion date the
    /// moment something starts needing it; making new schedules is left to the job.
    /// </summary>
    public static IReadOnlyList<TKey> FindStale<TKey>(
        IReadOnlyDictionary<TKey, DeletionReason> eligible,
        IReadOnlyDictionary<TKey, DeletionSchedule> scheduled)
        where TKey : notnull
    {
        return
        [
            .. scheduled
                .Where(x => !eligible.TryGetValue(x.Key, out var reason) || reason != x.Value.Reason)
                .Select(x => x.Key)
        ];
    }

    /// <summary>
    /// The rows to delete today: scheduled for today or earlier, and still eligible for the reason
    /// they were scheduled for. Asked again at deletion rather than trusted from the schedule, because
    /// a schedule is only cleared eventually and a deletion cannot be taken back.
    /// </summary>
    public static IReadOnlyList<TKey> FindDue<TKey>(
        IReadOnlyDictionary<TKey, DeletionReason> eligible,
        IReadOnlyDictionary<TKey, DeletionSchedule> scheduled,
        DateOnly today)
        where TKey : notnull
    {
        return
        [
            .. scheduled
                .Where(x => x.Value.Date <= today)
                .Where(x => eligible.TryGetValue(x.Key, out var reason) && reason == x.Value.Reason)
                .Select(x => x.Key)
        ];
    }
}
