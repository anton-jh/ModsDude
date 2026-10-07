using ModsDude.Client.Core.Stores;
using System.Windows.Threading;

namespace ModsDude.Client.Wpf.Shared;

public sealed class WpfStoreDispatcher(Dispatcher dispatcher) : IStoreDispatcher
{
    public Task InvokeAsync(Action action)
    {
        if (dispatcher.CheckAccess())
        {
            action();

            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }
}
