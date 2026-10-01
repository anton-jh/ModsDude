using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Wpf.Savegames;

public interface ISavegamePublishFlow
{
    /// <summary>
    /// Makes a savegame out of a save already on this disk: which slot, then the publish modal.
    /// Failures are reported here.
    /// </summary>
    /// <param name="preselectProfileId">
    /// The profile the modal opens on. Null opens it on the profile this game follows.
    /// </param>
    /// <param name="published">Called with the new savegame's id once it exists, so the caller can re-read.</param>
    Task PublishAsync(
        Repo repo,
        Guid? preselectProfileId,
        Func<Guid, Task> published,
        CancellationToken cancellationToken);
}
