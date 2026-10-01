using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Savegames;

/// <summary>Turns whatever is in a slot into a new savegame in the repo.</summary>
public interface ISavegamePublisher
{
    /// <param name="target">
    /// Which profile the new savegame follows and the revision its first snapshot declares, or null for
    /// a savegame that follows none.
    /// </param>
    /// <param name="keepPlaying">
    /// Whether to stay holding the save afterwards. False hands it straight back, which publishing to a
    /// profile the game is not on has to do.
    /// </param>
    /// <param name="progress">Which stage the bytes are in and how far through it they are.</param>
    /// <exception cref="Exceptions.UserFriendlyException">
    /// The game is running, or already holds a savegame that claims its mod folder.
    /// </exception>
    Task<SavegamePublishResult> PublishAsync(
        Game game,
        Guid repoId,
        SavegameSlotRef slot,
        string name,
        string? label,
        SavegamePublishTarget? target,
        bool keepPlaying,
        CancellationToken ct,
        IProgress<SavegameProgress>? progress = null);
}


/// <summary>
/// Which profile a savegame being published follows, and the revision its first snapshot declares -
/// see <see cref="SavegameRevisionRules.DeclaredRevisionFor"/>. One value because the server refuses
/// half of the pair.
/// </summary>
public readonly record struct SavegamePublishTarget(Guid ProfileId, int Revision);


public sealed record SavegamePublishResult(SavegameDto Savegame, SavegameLocalCopy LocalCopy);
