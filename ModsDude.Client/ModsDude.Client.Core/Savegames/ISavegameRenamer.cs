using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Savegames;

/// <summary>Writes a savegame's name into its slot, so the game's own menu and the repo agree on it.</summary>
public interface ISavegameRenamer
{
    /// <summary>
    /// Applies the adapter's rename edits to the slot. A name is decoration next to the bytes, so a
    /// failed edit is logged and the slot keeps its old name.
    /// </summary>
    void TryRename(ILocalSavegameAdapter adapter, SavegameTarget target, SavegameSlotId slot, string name);
}
