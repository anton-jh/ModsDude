using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameFiles;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Savegames;

public sealed class SavegameRenamer(IGameFileEditor fileEditor, ILogger<SavegameRenamer> logger) : ISavegameRenamer
{
    public void TryRename(ILocalSavegameAdapter adapter, SavegameTarget target, SavegameSlotId slot, string name)
    {
        var slotPath = adapter.GetSlotPath(target, slot);

        foreach (var edit in adapter.RenameSavegame(new SavegameRenameContext(target, slot, name)))
        {
            try
            {
                fileEditor.Apply(slotPath, edit);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Could not rename the savegame in slot {Slot}; it keeps its old name.", slot.Value);
            }
        }
    }
}
