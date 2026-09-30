using ModsDude.Client.Core.Persistence;
using System.Text.Json;

namespace ModsDude.Client.Core.Services;

public class ClientSettingsRepository(
    StateStore store)
{
    /// <inheritdoc cref="Store{T}.Read"/>
    public T Read<T>(Func<ClientSettings, T> read) => store.Read(state => read(state.Settings));

    /// <summary>A copy of the settings as they are now, for a caller that reads more than one value.</summary>
    public ClientSettings Snapshot()
        => Read(x => JsonSerializer.Deserialize<ClientSettings>(JsonSerializer.Serialize(x))!);

    /// <inheritdoc cref="Store{T}.Update"/>
    public void Update(Action<ClientSettings> update) => store.Update(state => update(state.Settings));
}
