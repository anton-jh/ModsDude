using Microsoft.Extensions.Logging;

namespace ModsDude.Client.Core.Stores;

/// <summary>
/// How a store reads from and writes to the server, so that every store keeps the same promises.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>One read per key at a time. A caller arriving while one is out waits for that one.</item>
/// <item>A read never lands on top of a write the store applied while the read was out. It is read
/// again instead, because its answer may predate the write.</item>
/// <item>Answers are applied on the <see cref="IStoreDispatcher"/> thread, and only for the user who
/// asked: <see cref="Reset"/> cancels every read and drops every answer still on its way.</item>
/// </list>
/// </remarks>
public sealed class StoreLoads<TKey>(IStoreDispatcher dispatcher, ILogger logger)
    where TKey : notnull
{
    private const int _maxAttempts = 5;

    private readonly Lock _lock = new();
    private readonly Dictionary<TKey, Task> _running = [];
    private readonly Dictionary<TKey, long> _writes = [];
    private CancellationTokenSource _lifetime = new();
    private long _epoch;


    /// <summary>
    /// Reads <paramref name="key"/> and applies the answer, or waits for the read already out.
    /// </summary>
    /// <param name="cancellationToken">Stops this caller waiting. The read itself goes on for any other caller.</param>
    public Task ReadAsync<T>(
        TKey key,
        Func<CancellationToken, Task<T>> read,
        Action<T> apply,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource? started = null;
        Task running;
        long epoch;
        CancellationToken lifetime;

        lock (_lock)
        {
            epoch = _epoch;
            lifetime = _lifetime.Token;

            if (_running.TryGetValue(key, out var existing) is false)
            {
                started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                existing = started.Task;
                _running[key] = existing;
            }

            running = existing;
        }

        if (started is not null)
        {
            // Owned by the slot every caller waits on: it never throws, it hands its outcome there.
            _ = CompleteReadAsync(started, key, read, apply, epoch, lifetime);
        }

        return running.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Sends a write and applies its answer to <paramref name="key"/>, unless the user changed while it
    /// was out.
    /// </summary>
    /// <returns>The server's answer, applied or not.</returns>
    public async Task<T> WriteAsync<T>(
        TKey key,
        Func<CancellationToken, Task<T>> send,
        Action<T> apply,
        CancellationToken cancellationToken)
    {
        var epoch = Epoch();
        var answer = await send(cancellationToken);

        await ApplyAsync(key, () => apply(answer), epoch);

        return answer;
    }

    /// <inheritdoc cref="WriteAsync{T}"/>
    public Task WriteAsync(
        TKey key,
        Func<CancellationToken, Task> send,
        Action apply,
        CancellationToken cancellationToken)
        => WriteAsync(
            key,
            async ct =>
            {
                await send(ct);

                return true;
            },
            _ => apply(),
            cancellationToken);

    /// <summary>
    /// Applies a change the store learnt of some other way than a read of <paramref name="key"/>, such
    /// as another store's write.
    /// </summary>
    public Task ApplyAsync(TKey key, Action apply)
        => ApplyAsync(key, apply, Epoch());

    /// <summary>
    /// Forgets everything for a user change: cancels every read, and drops every answer and write
    /// still on its way.
    /// </summary>
    public void Reset()
    {
        CancellationTokenSource previous;

        lock (_lock)
        {
            _epoch++;
            _running.Clear();
            _writes.Clear();

            previous = _lifetime;
            _lifetime = new CancellationTokenSource();
        }

        previous.Cancel();
        previous.Dispose();
    }


    private async Task CompleteReadAsync<T>(
        TaskCompletionSource slot,
        TKey key,
        Func<CancellationToken, Task<T>> read,
        Action<T> apply,
        long epoch,
        CancellationToken lifetime)
    {
        Exception? failure = null;

        try
        {
            await RunReadAsync(key, read, apply, epoch, lifetime);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        // Out of the running set before anybody learns how it ended, so a caller reacting to the
        // outcome starts a new read rather than joining this finished one.
        lock (_lock)
        {
            if (_running.TryGetValue(key, out var current) && current == slot.Task)
            {
                _running.Remove(key);
            }
        }

        switch (failure)
        {
            case null:
                slot.TrySetResult();
                break;

            case OperationCanceledException when lifetime.IsCancellationRequested:
                slot.TrySetCanceled(lifetime);
                break;

            default:
                slot.TrySetException(failure);
                break;
        }
    }

    private async Task RunReadAsync<T>(
        TKey key,
        Func<CancellationToken, Task<T>> read,
        Action<T> apply,
        long epoch,
        CancellationToken lifetime)
    {
        for (var attempt = 1; ; attempt++)
        {
            var writesBefore = WritesTo(key);
            var answer = await read(lifetime);
            var outcome = ReadOutcome.Superseded;

            await dispatcher.InvokeAsync(() =>
            {
                lock (_lock)
                {
                    if (_epoch != epoch)
                    {
                        outcome = ReadOutcome.UserChanged;

                        return;
                    }

                    if (_writes.GetValueOrDefault(key) != writesBefore)
                    {
                        return;
                    }
                }

                apply(answer);
                outcome = ReadOutcome.Applied;
            });

            switch (outcome)
            {
                case ReadOutcome.Applied:
                    return;

                case ReadOutcome.UserChanged:
                    throw new OperationCanceledException(lifetime);
            }

            if (attempt == _maxAttempts)
            {
                logger.LogWarning(
                    "Gave up reading {Key} after {Attempts} reads that each crossed a write; the next read brings it in.",
                    key,
                    attempt);

                return;
            }
        }
    }

    private Task ApplyAsync(TKey key, Action apply, long epoch)
        => dispatcher.InvokeAsync(() =>
        {
            lock (_lock)
            {
                if (_epoch != epoch)
                {
                    return;
                }

                _writes[key] = _writes.GetValueOrDefault(key) + 1;
            }

            apply();
        });

    private long Epoch()
    {
        lock (_lock)
        {
            return _epoch;
        }
    }

    private long WritesTo(TKey key)
    {
        lock (_lock)
        {
            return _writes.GetValueOrDefault(key);
        }
    }


    private enum ReadOutcome
    {
        Superseded,
        Applied,
        UserChanged
    }
}
