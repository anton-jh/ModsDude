using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Persistence;

namespace ModsDude.Client.Core.Services;

public class ClientSettingsRepository(
    StateStore store)
{
    public ClientSettings Settings => store.Get().Settings;


    public void Save()
    {
        store.Save();
    }
}
