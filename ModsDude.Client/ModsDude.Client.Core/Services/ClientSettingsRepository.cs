using ModsDude.Client.Core.Persistence;
using System.Text.Json;

namespace ModsDude.Client.Core.Services;

public class ClientSettingsRepository(
    IStateStore store) : IClientSettingsRepository
{
    public T Read<T>(Func<ClientSettings, T> read) => store.Read(state => read(state.Settings));

    public ClientSettings Snapshot()
        => Read(x => JsonSerializer.Deserialize<ClientSettings>(JsonSerializer.Serialize(x))!);

    public void Update(Action<ClientSettings> update) => store.Update(state => update(state.Settings));
}
