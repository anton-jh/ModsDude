namespace ModsDude.Client.Core.Helpers;

/// <summary>
/// An <see cref="IProgress{T}"/> that runs its callback on whichever thread reported.
/// </summary>
/// <remarks>
/// <see cref="Progress{T}"/> posts to the synchronization context it was created on, which for a
/// byte counter means one UI-thread message per buffer and reports that arrive out of step with the
/// work. Everything that consumes these - the strip, which coalesces on a timer, and the tests, which
/// assert on order - wants the call made where the byte was counted.
/// </remarks>
public sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
