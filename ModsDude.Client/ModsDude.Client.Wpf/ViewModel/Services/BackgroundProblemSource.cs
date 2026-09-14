using ModsDude.Client.Core.Notices;
using ModsDude.Client.Wpf.Diagnostics;

namespace ModsDude.Client.Wpf.ViewModel.Services;

/// <summary>
/// The quiet end of the column: work the app absorbed on purpose, counted so that absorbing it does
/// not also mean hiding it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A source of notices rather than a card.</b> It used to draw one aggregated box under the drift
/// card, headlined "Some background work did not finish" whenever more than one kind was counted -
/// which is a sentence that tells nobody anything. One notice per kind says what actually stopped
/// working, and the column is what made room for that.
/// </para>
/// <para>
/// <b>Still aggregated within a kind</b>, because the failures come in batches: one storage
/// container that does not exist is not 2,000 problems, it is one problem seen 2,000 times.
/// </para>
/// <para>
/// <b>Dismissal here is a cooldown, not a signature</b> - the one place in the column where that is
/// true, and the reason is the counting. Every other notice is dismissed against what it says, so a
/// changed sentence brings it back; that rule applied to a count which ticks upward during an import
/// would bring the card back on the very next failure, which is not a dismissal at all. Counting
/// continues underneath, so what comes back afterwards shows the full total.
/// </para>
/// </remarks>
public sealed class BackgroundProblemSource(TimeProvider? timeProvider = null) : IBackgroundProblemReporter
{
    /// <summary>Notice keys are prefixed with this, so the column knows whose dismissal to route here.</summary>
    public const string KeyPrefix = "background/";

    /// <summary>
    /// Long enough that a dismissal means something during a long import, short enough that a problem
    /// which is still happening comes back before the user has finished the session.
    /// </summary>
    private static readonly TimeSpan _cooldown = TimeSpan.FromMinutes(10);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly Lock _lock = new();
    private readonly Dictionary<BackgroundProblem, int> _counts = [];

    private DateTimeOffset? _dismissedAt;


    /// <summary>Raised when the column would say something different. Fired from whatever thread reported.</summary>
    public event EventHandler? Changed;


    public void Report(BackgroundProblem problem)
    {
        lock (_lock)
        {
            _counts[problem] = _counts.GetValueOrDefault(problem) + 1;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Whether this key is one of these, and therefore dismissed by cooldown rather than by signature.</summary>
    public static bool Owns(string key) => key.StartsWith(KeyPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Starts the cooldown. Every kind at once, because they share it - the user is saying "stop
    /// telling me about absorbed failures for a while", not picking one of three.
    /// </summary>
    public void Dismiss()
    {
        lock (_lock)
        {
            _dismissedAt = _timeProvider.GetUtcNow();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }


    public IReadOnlyList<Notice> Build()
    {
        KeyValuePair<BackgroundProblem, int>[] snapshot;

        lock (_lock)
        {
            if (_dismissedAt is DateTimeOffset dismissed
                && _timeProvider.GetUtcNow() - dismissed < _cooldown)
            {
                return [];
            }

            snapshot = [.. _counts.Where(x => x.Value > 0).OrderBy(x => x.Key)];
        }

        return [.. snapshot.Select(x => Build(x.Key, x.Value))];
    }


    private static Notice Build(BackgroundProblem problem, int count)
    {
        var headline = problem switch
        {
            BackgroundProblem.ImageUpload => "Mod images could not be uploaded",
            BackgroundProblem.ImageDisplay => "Some images could not be shown",
            _ => "Some rows could not finish loading"
        };

        // Said in terms of the consequence, because "an upload failed" means nothing to somebody
        // looking at a list of mods with initials where the pictures should be.
        var body = problem switch
        {
            BackgroundProblem.ImageUpload => count == 1
                ? "One mod's images could not be uploaded, so it shows initials for everybody until a "
                    + "client holding the file tries again."
                : $"{count} mods' images could not be uploaded, so they show initials for everybody until "
                    + "a client holding the files tries again.",

            BackgroundProblem.ImageDisplay => count == 1
                ? "1 image could not be shown and was replaced with initials."
                : $"{count} images could not be shown and were replaced with initials.",

            _ => count == 1
                ? "1 row could not finish loading."
                : $"{count} rows could not finish loading."
        };

        return new Notice(
            $"{KeyPrefix}{problem}",
            // The count is deliberately out of the signature: these are dismissed on a cooldown, and a
            // signature that moved with every absorbed failure would undo it instantly. See the class.
            $"{KeyPrefix}{problem}",
            NoticeSeverity.Info,
            headline)
        {
            Body = body,
            Footnote = $"The log has the details. It is in {LogFolder.Path}.",
            Actions = [new(NoticeActionKind.OpenLog, "Open log folder")]
        };
    }
}
