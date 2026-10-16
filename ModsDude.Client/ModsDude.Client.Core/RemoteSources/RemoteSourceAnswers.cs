using ModsDude.Client.Core.GameAdapters;

namespace ModsDude.Client.Core.RemoteSources;

/// <summary>What one remote source has answered on a page.</summary>
/// <param name="Current">Its latest answer per mod.</param>
/// <param name="Retained">
/// Every version it has answered with on this page, including those a later answer replaced, so a pin
/// made on one still resolves.
/// </param>
public sealed record RemoteSourceAnswers(
    IRemoteModSource Source,
    bool IsEnabled,
    IReadOnlyList<RemoteModVersion> Current,
    IReadOnlyList<RemoteModVersion> Retained);
