using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameFiles;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Savegames;

public sealed class SavegameRenamer(IGameFileEditor fileEditor, ILogger<SavegameRenamer> logger) : ISavegameRenamer
{
    public IReadOnlyList<GameFileEdit> Plan(ILocalSavegameAdapter adapter, SavegameTarget target, SavegameSlotId slot, string? name)
        => name is null ? [] : adapter.RenameSavegame(new SavegameRenameContext(target, slot, name));

    public bool TryApply(ILocalSavegameAdapter adapter, SavegameTarget target, SavegameSlotId slot, IReadOnlyList<GameFileEdit> edits)
    {
        var slotPath = adapter.GetSlotPath(target, slot);
        var applied = true;

        foreach (var edit in edits)
        {
            try
            {
                fileEditor.Apply(slotPath, edit);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Could not rename the savegame in slot {Slot}; it keeps its old name.", slot.Value);

                applied = false;
            }
        }

        return applied;
    }
}
