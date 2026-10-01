using ModsDude.Client.Core.Notices;

namespace ModsDude.Client.Wpf.Shell.Notices;

public interface IBackgroundProblemSource : IBackgroundProblemReporter
{
    /// <summary>Raised when the column would say something different. Fired from whatever thread reported.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Starts the cooldown. Every kind at once, because they share it - the user is saying "stop
    /// telling me about absorbed failures for a while", not picking one of three.
    /// </summary>
    void Dismiss();

    IReadOnlyList<Notice> Build();
}
