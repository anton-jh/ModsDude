using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Savegames;

/// <summary>Writes a savegame's name into its slot, so the game's own menu and the repo agree on it.</summary>
/// <remarks>
/// A rename reaches the slot only once the server has accepted the snapshot it is part of: until then
/// it is an overlay on the packed copy, so a refused check-in leaves the slot exactly as it was.
/// </remarks>
public interface ISavegameRenamer
{
    /// <summary>The edits that give the slot this name, for packing into the archived copy.</summary>
    IReadOnlyList<GameFileEdit> Plan(ILocalSavegameAdapter adapter, SavegameTarget target, SavegameSlotId slot, string? name);

    /// <summary>
    /// Applies the edits to the slot itself. A name is decoration next to the bytes, so a failed edit
    /// is logged and the slot keeps its old name.
    /// </summary>
    /// <returns>Whether every edit was applied.</returns>
    bool TryApply(ILocalSavegameAdapter adapter, SavegameTarget target, SavegameSlotId slot, IReadOnlyList<GameFileEdit> edits);
}
