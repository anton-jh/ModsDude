using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Savegames;

/// <summary>The claims and local holds on savegames, apart from the bytes in their slots.</summary>
public interface ISavegameHolds
{
    /// <summary>
    /// Gives the claim back on the server, then forgets the binding. Server first: a binding cleared
    /// against a claim still open would leave the save unclaimable by anybody.
    /// </summary>
    Task ReleaseAsync(Game game, Guid repoId, Guid savegameId, CancellationToken ct);

    /// <summary>
    /// Makes a past savegame current again, and lets go of the revision it pinned the mod folder to in
    /// every given game holding it. The pins are cleared only after the server accepts the swap.
    /// </summary>
    /// <param name="games">Every installation that might be holding it. None is fine.</param>
    Task<MakeSavegameCurrentResponse> MakeCurrentAsync(IReadOnlyList<Game> games, SavegameDto savegame, CancellationToken ct);

    /// <summary>
    /// Cuts every local tie to a savegame. The slot's contents are not touched and the server is not
    /// told, so a claim that still exists stays standing.
    /// </summary>
    /// <returns>False where this game was holding no such savegame.</returns>
    bool Forget(Game game, Guid savegameId);

    /// <summary>
    /// The savegames this game holds in a folder its settings no longer name. Empty where no loaded repo
    /// serves the game, since nothing can be said about folders nobody can enumerate.
    /// </summary>
    IReadOnlyList<SavegameCheckoutBinding> GetUnreachableHolds(Game game);
}
