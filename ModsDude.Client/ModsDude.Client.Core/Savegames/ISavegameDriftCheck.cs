using ModsDude.Client.Core.GameAdapters;

namespace ModsDude.Client.Core.Savegames;

/// <summary>Which held savegames have stopped agreeing with the server, for the drift notice.</summary>
public interface ISavegameDriftCheck
{
    /// <summary>
    /// Checks every hold of the game, each against its own target's manifest. A game no loaded repo
    /// serves, and a hold whose folder the settings no longer name, report nothing.
    /// </summary>
    Task<IReadOnlyList<SavegameDrift>> CheckDriftAsync(GameIdentity game, CancellationToken ct);
}
