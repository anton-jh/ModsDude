using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Wpf.Savegames;

public interface ISavegamePublishFlow
{
    /// <summary>
    /// Makes a savegame out of a save already on this disk: which slot, the publish itself, and
    /// whatever keeping it needs first. Failures are reported here.
    /// </summary>
    /// <param name="preselectProfileId">
    /// The profile the publish step opens on. Null opens it on the profile this game follows.
    /// </param>
    /// <param name="changed">
    /// Called whenever the repo moved under the caller, with the new savegame's id once it exists, so
    /// the caller can re-read.
    /// </param>
    Task PublishAsync(
        Repo repo,
        Guid? preselectProfileId,
        Func<Guid?, Task> changed,
        CancellationToken cancellationToken);
}
