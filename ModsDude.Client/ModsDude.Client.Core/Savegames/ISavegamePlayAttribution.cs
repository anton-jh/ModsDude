using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Savegames;

/// <summary>
/// Which profile revision play on a held savegame happened on. Attributed by observation: when a slot's
/// bytes have moved since the last look, the play is credited to the revision its mod folder is on now.
/// </summary>
public interface ISavegamePlayAttribution
{
    /// <summary>
    /// Observes every savegame held in this target. Must run before an apply rewrites the target's
    /// manifest, since the manifest is the only record of the revision the play ran on.
    /// </summary>
    Task ObserveAsync(ModTargetRef target, CancellationToken ct);

    /// <summary>
    /// One observation of a held savegame whose slot now hashes to <paramref name="currentContentHash"/>.
    /// Writes the binding only when the bytes moved.
    /// </summary>
    /// <returns>The binding as it now stands.</returns>
    SavegameCheckoutBinding Observe(GameIdentity game, SavegameCheckoutBinding binding, string currentContentHash);

    /// <summary>
    /// Which revision a check-in of this binding would record: the last observed play, else the folder's
    /// revision where it is on the savegame's profile, else the revision recorded at check-out. Null for
    /// a savegame that follows no profile.
    /// </summary>
    int? FindPlayedRevision(GameIdentity game, SavegameCheckoutBinding binding);

    /// <summary><see cref="FindPlayedRevision"/> for a savegame this game holds, or null where it holds none.</summary>
    int? GetPlayedRevision(Game game, Guid savegameId);
}
