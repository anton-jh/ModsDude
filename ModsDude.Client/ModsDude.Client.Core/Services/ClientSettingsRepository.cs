using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Persistence;

namespace ModsDude.Client.Core.Services;

public class ClientSettingsRepository(
    StateStore store)
{
    public ClientSettings Settings => store.Get().Settings;


    public bool IsSourceDisabled(ModSourceId sourceId)
    {
        return Settings.DisabledSources.Contains(sourceId);
    }

    /// <summary>
    /// Remembers that a standing source should not be scanned. Someone who never wants Downloads
    /// looked at should not have to say so every session.
    /// </summary>
    public void SetSourceDisabled(ModSourceId sourceId, bool disabled)
    {
        var changed = disabled
            ? Settings.DisabledSources.Add(sourceId)
            : Settings.DisabledSources.Remove(sourceId);

        if (changed)
        {
            Save();
        }
    }

    public void Save()
    {
        store.Save();
    }
}
