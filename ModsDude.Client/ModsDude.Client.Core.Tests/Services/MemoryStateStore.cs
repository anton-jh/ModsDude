using ModsDude.Client.Core.Persistence;

namespace ModsDude.Client.Core.Tests.Services;

internal sealed class MemoryStateStore : IStateStore
{
    private readonly LocalState _state = new();

    public TResult Read<TResult>(Func<LocalState, TResult> read) => read(_state);

    public void Update(Action<LocalState> update) => update(_state);

    public bool UpdateIf(Func<LocalState, bool> update) => update(_state);
}
