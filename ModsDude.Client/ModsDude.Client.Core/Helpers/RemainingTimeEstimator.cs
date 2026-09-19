namespace ModsDude.Client.Core.Helpers;

/// <summary>
/// Turns a stream of "this far of that much" reports into a guess at how long is left.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rate is taken over a trailing window, not since the start and not between two reports.</b>
/// Since the start is wrong the moment a speed limit is lifted or a large file gives way to small
/// ones, and it goes on being wrong for as long as the work is long; between two reports is wrong
/// every time work arrives in lumps - a sync that finishes one mod in ten seconds reads as "no
/// progress" for nine of them and "finished" for the tenth. A window of <see cref="Window"/> is long
/// enough to smooth the lumps and short enough to follow a change of pace.
/// </para>
/// <para>
/// <b>It restarts whenever the distance does.</b> A different stage, a different total, or a count
/// that went backwards is a different piece of work, and its speed says nothing about the last one's -
/// packing a slot and uploading the result are not the same rate over the same bytes.
/// </para>
/// <para>
/// <b>It stays quiet until it has something to base a number on</b>: <see cref="Warmup"/> of watching,
/// and some movement in that time. A guess made from the first second of a transfer is a number that
/// then changes by a factor of ten, which is worse than none. Not thread-safe; the caller serialises.
/// </para>
/// </remarks>
public sealed class RemainingTimeEstimator(TimeProvider? clock = null)
{
    /// <summary>How much history the rate is taken over.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(30);

    /// <summary>How long a piece of work is watched before any estimate is offered.</summary>
    public static readonly TimeSpan Warmup = TimeSpan.FromSeconds(4);

    /// <summary>How far apart the samples in the window are, so a report per buffer does not fill it.</summary>
    private static readonly TimeSpan SampleEvery = TimeSpan.FromMilliseconds(500);

    /// <summary>Past this it is not an estimate, it is a shrug.</summary>
    private static readonly TimeSpan Ceiling = TimeSpan.FromHours(24);


    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Queue<(long At, long Completed)> _samples = [];

    private string? _stage;
    private long _total;
    private long _startedAt;
    private long _startedCompleted;
    private long _lastCompleted;
    private bool _active;


    /// <summary>
    /// Records where the work has got to. A <paramref name="total"/> of zero or less is "not
    /// countable", which leaves nothing to estimate and clears what was there.
    /// </summary>
    /// <param name="stage">
    /// Anything that changes when the work moves on to something else, such as the phase's name. Null
    /// is a stage of its own.
    /// </param>
    public void Observe(string? stage, long completed, long total)
    {
        if (total <= 0)
        {
            _active = false;
            _samples.Clear();

            return;
        }

        var now = _clock.GetTimestamp();

        if (_active is false || stage != _stage || total != _total || completed < _lastCompleted)
        {
            Restart(stage, completed, total, now);

            return;
        }

        _lastCompleted = completed;

        if (_clock.GetElapsedTime(_samples.Last().At, now) >= SampleEvery)
        {
            _samples.Enqueue((now, completed));
        }

        // Drop what has aged out, but only while the next sample is also old enough to stand in for
        // the edge of the window - so the oldest one kept is always the newest that is past it.
        while (_samples.Count > 1 && _clock.GetElapsedTime(_samples.ElementAt(1).At, now) >= Window)
        {
            _samples.Dequeue();
        }
    }

    /// <summary>Forgets everything, for work that ended or is starting over.</summary>
    public void Reset()
    {
        _active = false;
        _samples.Clear();
    }

    /// <summary>The estimate, or null while there is not enough to give one.</summary>
    public TimeSpan? Remaining
    {
        get
        {
            if (_active is false || _lastCompleted >= _total || _lastCompleted <= _startedCompleted)
            {
                return null;
            }

            // Measured to now rather than to the last report, so work that has gone quiet stretches
            // its own estimate instead of freezing on the last figure it gave.
            var now = _clock.GetTimestamp();

            if (_clock.GetElapsedTime(_startedAt, now) < Warmup)
            {
                return null;
            }

            var (at, completed) = _samples.Peek();
            var span = _clock.GetElapsedTime(at, now);

            if (span <= TimeSpan.Zero || _lastCompleted <= completed)
            {
                return null;
            }

            var perSecond = (_lastCompleted - completed) / span.TotalSeconds;
            var seconds = (_total - _lastCompleted) / perSecond;

            return seconds < Ceiling.TotalSeconds ? TimeSpan.FromSeconds(seconds) : null;
        }
    }


    private void Restart(string? stage, long completed, long total, long now)
    {
        _active = true;
        _stage = stage;
        _total = total;
        _startedAt = now;
        _startedCompleted = completed;
        _lastCompleted = completed;

        _samples.Clear();
        _samples.Enqueue((now, completed));
    }


    /// <summary>
    /// "about 3 min left". Rounded coarsely on purpose - a guess written to the second is a claim it
    /// cannot back, and a figure that changes every tick is the strip flickering again.
    /// </summary>
    public static string Describe(TimeSpan remaining)
    {
        var seconds = remaining.TotalSeconds;

        if (seconds < 10)
        {
            return "a few seconds left";
        }

        if (seconds < 55)
        {
            return $"about {Math.Round(seconds / 5) * 5:0} s left";
        }

        var minutes = (int)Math.Ceiling(seconds / 60);

        if (minutes < 60)
        {
            return $"about {minutes} min left";
        }

        var rounded = (int)(Math.Round(minutes / 5d) * 5);

        return rounded % 60 is 0
            ? $"about {rounded / 60} h left"
            : $"about {rounded / 60} h {rounded % 60} min left";
    }
}
