namespace ModsDude.Client.Core.Updates;

/// <summary>
/// Whether a newer version of the app has been downloaded and is waiting for a restart.
/// </summary>
/// <remarks>
/// <para>
/// A seam rather than the updater itself, for the reason <c>INoticeEnvironment</c> is one: what the
/// column says about an update is a function of one fact, and the thing that finds out that fact
/// - a network client that only exists in an installed copy - has no business being reachable from the
/// code that decides what to tell the user.
/// </para>
/// <para>
/// <b>Ready, not available.</b> A version is announced once it is on disk and installing it costs a
/// restart and nothing else. Telling somebody about an update they cannot yet take would be a notice
/// whose button does nothing.
/// </para>
/// </remarks>
public interface IUpdateStatus
{
    /// <summary>The version waiting to be installed, or null where there is none - including in a copy that cannot update.</summary>
    string? ReadyVersion { get; }

    /// <summary>Raised, from any thread, when <see cref="ReadyVersion"/> changed.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Restarts into the downloaded version. Does nothing where there is none, or where the user, asked
    /// about work that is still running, would rather not.
    /// </summary>
    Task RestartAsync();
}
