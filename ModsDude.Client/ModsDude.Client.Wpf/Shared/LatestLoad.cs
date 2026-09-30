namespace ModsDude.Client.Wpf.Shared;

/// <summary>
/// A load a selection drives: starting one cancels the one before it, only the newest one's answer is
/// shown, and the loading flag stays on until the newest one is done.
/// </summary>
/// <remarks>
/// Driven from the UI thread, so the bookkeeping needs no lock: every continuation runs there too.
/// </remarks>
public sealed class LatestLoad(Action<bool> setLoading, CancellationToken lifetime = default)
{
    private CancellationTokenSource? _current;


    /// <param name="fetch">Reads what to show. Cancelled as soon as a newer load starts, or the page goes.</param>
    /// <param name="show">Shows it. Only ever called for the newest load.</param>
    /// <param name="fail">Says why the newest load failed. A superseded load's failure is nobody's business.</param>
    public async Task RunAsync<T>(Func<CancellationToken, Task<T>> fetch, Action<T> show, Func<Exception, Task> fail)
    {
        _current?.Cancel();

        using var mine = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        _current = mine;

        setLoading(true);

        try
        {
            var result = await fetch(mine.Token);

            if (_current == mine)
            {
                show(result);
            }
        }
        catch (Exception exception) when (_current == mine && mine.IsCancellationRequested is false)
        {
            // The flag goes off before the dialog, which stays up until it is closed.
            setLoading(false);

            await fail(exception);
        }
        catch (Exception)
        {
            // Superseded or cancelled: a newer load owns the flag and the view.
        }
        finally
        {
            if (_current == mine)
            {
                _current = null;
                setLoading(false);
            }
        }
    }

    /// <summary>Stops whatever is loading, for a selection that now has nothing to show.</summary>
    public void Cancel()
    {
        _current?.Cancel();
        _current = null;
        setLoading(false);
    }
}
