using ModsDude.Client.Core.Stores;

namespace ModsDude.Client.Core.Tests.Stores;

/// <summary>Runs every change at once, on whichever thread asked: a test has no UI thread to wait for.</summary>
public sealed class InlineStoreDispatcher : IStoreDispatcher
{
    public static InlineStoreDispatcher Instance { get; } = new();

    public Task InvokeAsync(Action action)
    {
        action();

        return Task.CompletedTask;
    }
}
