using ModsDude.Client.Core.Persistence;

namespace ModsDude.Client.Core.Transfers;

/// <summary>Which way bytes move between this machine and storage.</summary>
[Flags]
public enum TransferDirection
{
    None = 0,
    Download = 1,
    Upload = 2
}


/// <summary>
/// The user's speed limits, one per direction, shared by every transfer in the process.
/// </summary>
/// <remarks>
/// <para>
/// <b>Process-wide rather than per transfer</b>, because a limit is a promise about the line: "leave
/// me 50 Mb/s for the call I am on" is broken just as surely by four downloads at 20 each as by one at
/// 80. Everything that moves bytes draws from the same bucket for its direction.
/// </para>
/// <para>
/// Changed in place when settings are saved, and taking effect on the next read or write of every
/// transfer already running - nobody should have to restart a 1.4 GB download to slow it down.
/// </para>
/// </remarks>
public sealed class TransferLimits
{
    public TransferLimits()
    {
        Download.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        Upload.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }


    /// <summary>Raised whenever either limit changes, from whichever thread applied it.</summary>
    public event EventHandler? Changed;

    public TransferRateLimiter Download { get; } = new();
    public TransferRateLimiter Upload { get; } = new();


    public static TransferLimits From(TransferLimitSettings settings)
    {
        var limits = new TransferLimits();
        limits.Apply(settings);

        return limits;
    }

    public void Apply(TransferLimitSettings settings)
    {
        Download.BytesPerSecond = settings.DownloadBytesPerSecond;
        Upload.BytesPerSecond = settings.UploadBytesPerSecond;
    }

    /// <summary>The limit on <paramref name="direction"/> in bytes per second, or null for none.</summary>
    public long? Get(TransferDirection direction) => direction switch
    {
        TransferDirection.Download => Download.BytesPerSecond,
        TransferDirection.Upload => Upload.BytesPerSecond,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "One direction at a time.")
    };
}


/// <summary>
/// A token bucket for one direction: transfers say how many bytes they have just moved and are held
/// back for as long as that takes to pay off.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reservations, not polling.</b> Every caller takes its bytes out of the bucket at once, going
/// into debt if it has to, and waits exactly as long as the debt takes to clear at the current rate.
/// Several transfers therefore queue behind each other in the order they asked, and the total over
/// any second is the limit whichever of them is moving.
/// </para>
/// <para>
/// <b>It also caps connections.</b> Parallel range requests exist to beat a single connection's
/// ceiling, and under a limit there is no ceiling to beat - there is only the limit, divided so thinly
/// between eight connections that each one crawls. Storage times out a request that moves too slowly,
/// so a limited download opens only as many connections as can each be kept reasonably busy.
/// </para>
/// </remarks>
public sealed class TransferRateLimiter
{
    /// <summary>What each connection should be able to move under a limit, at the least.</summary>
    private const long _bytesPerSecondPerConnection = 256 * 1024;

    /// <summary>
    /// How far ahead of the rate a burst may run after a pause - a quarter of a second of transfer.
    /// Small enough that the limit holds over any interval a router's graph would show.
    /// </summary>
    private static readonly TimeSpan _burst = TimeSpan.FromMilliseconds(250);

    private readonly Lock _gate = new();
    private readonly TimeProvider _time;

    private long? _bytesPerSecond;

    /// <summary>Read without the lock, so an unlimited transfer never takes it.</summary>
    private volatile bool _limited;
    private double _available;
    private long _refilledAt;
    private int _connections;
    private TaskCompletionSource _connectionFreed = NewSignal();


    public TransferRateLimiter(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        _refilledAt = _time.GetTimestamp();
    }


    public event EventHandler? Changed;


    /// <summary>Null for no limit, which is the default.</summary>
    public long? BytesPerSecond
    {
        get
        {
            lock (_gate)
            {
                return _bytesPerSecond;
            }
        }
        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A limit has to be positive; null is no limit.");
            }

            lock (_gate)
            {
                if (_bytesPerSecond == value)
                {
                    return;
                }

                _bytesPerSecond = value;
                _limited = value is not null;

                // A fresh start at the new rate. Debt run up under a lower limit would otherwise go on
                // holding transfers back after the user has raised it.
                _available = 0;
                _refilledAt = _time.GetTimestamp();

                Signal();
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>How many connections a transfer in this direction may hold at once right now.</summary>
    public int ConnectionCap
    {
        get
        {
            lock (_gate)
            {
                return GetConnectionCap();
            }
        }
    }


    /// <summary>
    /// Accounts for <paramref name="bytes"/> just moved, and returns once the limit allows more.
    /// </summary>
    /// <remarks>Free when there is no limit: no lock, no timer, no allocation.</remarks>
    public ValueTask ConsumeAsync(int bytes, CancellationToken cancellationToken)
    {
        if (_limited is false || bytes <= 0)
        {
            return ValueTask.CompletedTask;
        }

        TimeSpan wait;

        lock (_gate)
        {
            if (_bytesPerSecond is not long rate)
            {
                return ValueTask.CompletedTask;
            }

            Refill(rate);

            _available -= bytes;

            if (_available >= 0)
            {
                return ValueTask.CompletedTask;
            }

            wait = TimeSpan.FromSeconds(-_available / rate);
        }

        return new ValueTask(Task.Delay(wait, _time, cancellationToken));
    }

    /// <summary>
    /// Waits for a connection under <see cref="ConnectionCap"/>. <b>Dispose the result</b> to hand it
    /// back.
    /// </summary>
    public async ValueTask<IDisposable> AcquireConnectionAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task freed;

            lock (_gate)
            {
                if (_connections < GetConnectionCap())
                {
                    _connections++;

                    return new Connection(this);
                }

                freed = _connectionFreed.Task;
            }

            await freed.WaitAsync(cancellationToken);
        }
    }


    private int GetConnectionCap()
    {
        return _bytesPerSecond is long rate
            ? (int)Math.Clamp(rate / _bytesPerSecondPerConnection, 1, int.MaxValue)
            : int.MaxValue;
    }

    private void Refill(long rate)
    {
        var now = _time.GetTimestamp();
        var elapsed = _time.GetElapsedTime(_refilledAt, now);

        _refilledAt = now;
        _available = Math.Min(_available + elapsed.TotalSeconds * rate, rate * _burst.TotalSeconds);
    }

    private void Release()
    {
        lock (_gate)
        {
            _connections--;
            Signal();
        }
    }

    /// <summary>Wakes every waiter to look again. Under the lock.</summary>
    private void Signal()
    {
        var freed = _connectionFreed;
        _connectionFreed = NewSignal();
        freed.TrySetResult();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);


    private sealed class Connection(TransferRateLimiter owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Release();
            }
        }
    }
}
