using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Notices;

namespace ModsDude.Client.Core.Connectivity;

/// <summary>What the app was trying to reach when nothing answered.</summary>
/// <remarks>The declaration order is the order their notices are drawn in.</remarks>
public enum ConnectionTarget
{
    /// <summary>The identity provider, which has to answer before anything else can be asked.</summary>
    SignIn,

    /// <summary>The ModsDude server. Its outage is the offline state rather than a notice.</summary>
    Server
}


/// <summary>
/// Keeps trying what the app needs until something answers, and says so while it does: in the notice
/// column for sign-in, and through <see cref="GetOutage"/> for the offline state.
/// </summary>
/// <remarks>
/// <para>
/// <b>On its own, because there is nobody to ask.</b> The server restarting and a network that comes
/// up a few seconds after sign-in are both over before anybody could have decided anything, and an
/// error dialog for them left the app on an empty sidebar - or on the sign-in page for good - until
/// somebody restarted it. <see cref="RetryNow"/> is there for the person who has just fixed their
/// connection and does not want to wait out the interval.
/// </para>
/// <para>
/// <b>Only failures that waiting can fix.</b> See <see cref="ConnectionFailure"/>. Anything else ends
/// the loop and is thrown to the caller, which shows it the way it always did.
/// </para>
/// <para>
/// <b>A notice, not a dialog, and one that cannot be dismissed.</b> It reports a state rather than an
/// event: waving it away would not make sign-in happen, and nothing else on screen explains why it
/// has not. Pending rather than Warning, so a start at sign-in with the network still coming up
/// is not announced as a Windows toast.
/// </para>
/// </remarks>
public sealed class ConnectionRetry(
    ILogger<ConnectionRetry> logger,
    TimeProvider timeProvider,
    Func<Exception, bool>? alsoConnectionFailure = null) : IConnectionRetry
{
    /// <summary>Notice keys are prefixed with this.</summary>
    public const string KeyPrefix = "connection/";

    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly Lock _lock = new();
    private readonly Dictionary<ConnectionTarget, ConnectionOutage> _outages = [];

    private TaskCompletionSource _wake = NewWake();


    public event EventHandler? Changed;


    /// <summary>
    /// How long to wait after this many failures in a row: quickly at first, for the blip, then no
    /// more often than once a minute, for the outage.
    /// </summary>
    public static TimeSpan DelayAfter(int failures) => failures switch
    {
        <= 1 => TimeSpan.FromSeconds(5),
        2 => TimeSpan.FromSeconds(10),
        3 => TimeSpan.FromSeconds(20),
        4 => TimeSpan.FromSeconds(30),
        _ => TimeSpan.FromSeconds(60)
    };


    public async Task<bool> RunAsync(
        ConnectionTarget target,
        Func<CancellationToken, Task> attempt,
        CancellationToken cancellationToken)
    {
        var failures = 0;
        ConnectionOutage? outage = null;

        try
        {
            while (true)
            {
                TimeSpan delay;

                try
                {
                    await attempt(cancellationToken);

                    return true;
                }
                catch (Exception) when (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }
                catch (Exception exception) when (ConnectionFailure.Is(exception, alsoConnectionFailure))
                {
                    failures++;
                    delay = DelayAfter(failures);

                    logger.LogWarning(exception, "Could not reach {Target} (attempt {Attempt}); trying again in {Delay}.",
                        target, failures, delay);
                }

                outage = Set(target, new ConnectionOutage(failures, _timeProvider.GetUtcNow() + delay));

                if (await WaitAsync(delay, cancellationToken) is false)
                {
                    return false;
                }

                // Said before the attempt rather than after it, so pressing Retry now visibly does something.
                outage = Set(target, new ConnectionOutage(failures, NextAttempt: null));
            }
        }
        finally
        {
            if (outage is not null)
            {
                Clear(target, outage);
            }
        }
    }

    public void RetryNow()
    {
        TaskCompletionSource woken;

        lock (_lock)
        {
            woken = _wake;
            _wake = NewWake();
        }

        woken.TrySetResult();
    }

    public ConnectionOutage? GetOutage(ConnectionTarget target)
    {
        lock (_lock)
        {
            return _outages.GetValueOrDefault(target);
        }
    }

    public IReadOnlyList<Notice> Build()
    {
        return GetOutage(ConnectionTarget.SignIn) is { } outage ? [DescribeSignIn(outage)] : [];
    }


    /// <returns>False where the wait was cancelled rather than finished or cut short.</returns>
    private async Task<bool> WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        Task wake;

        lock (_lock)
        {
            wake = _wake.Task;
        }

        using var sleeping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        await Task.WhenAny(Task.Delay(delay, _timeProvider, sleeping.Token), wake);

        // Stops the timer where the wait was cut short, rather than leaving it to fire for nobody.
        sleeping.Cancel();

        return cancellationToken.IsCancellationRequested is false;
    }

    private ConnectionOutage Set(ConnectionTarget target, ConnectionOutage outage)
    {
        lock (_lock)
        {
            _outages[target] = outage;
        }

        Changed?.Invoke(this, EventArgs.Empty);

        return outage;
    }

    /// <summary>
    /// Only where the outage recorded is still this run's. A replaced shell starts a second run for the
    /// same target while the first is winding down, and the first finishing must not clear the second's.
    /// </summary>
    private void Clear(ConnectionTarget target, ConnectionOutage outage)
    {
        lock (_lock)
        {
            if (_outages.TryGetValue(target, out var current) is false || ReferenceEquals(current, outage) is false)
            {
                return;
            }

            _outages.Remove(target);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private Notice DescribeSignIn(ConnectionOutage outage)
    {
        var tried = outage.Failures == 1 ? "Tried once." : $"Tried {outage.Failures} times.";

        var next = outage.NextAttempt is DateTimeOffset at
            ? $"Trying again at {TimeZoneInfo.ConvertTime(at, _timeProvider.LocalTimeZone):HH:mm:ss}."
            : "Trying again now...";

        var key = $"{KeyPrefix}{ConnectionTarget.SignIn}";

        return new Notice(key, key, NoticeSeverity.Pending, "Could not sign in")
        {
            Body = "The sign-in service did not answer, so nothing can be loaded yet. This is tried again on "
                + "its own - if it goes on, check this machine's internet connection.",
            Footnote = $"{tried} {next}",
            CanDismiss = false,
            Actions = [new(NoticeActionKind.RetryConnection, "Retry now") { IsPrimary = true }]
        };
    }

    private static TaskCompletionSource NewWake() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}


/// <param name="NextAttempt">Null while an attempt is under way.</param>
public sealed record ConnectionOutage(int Failures, DateTimeOffset? NextAttempt);
