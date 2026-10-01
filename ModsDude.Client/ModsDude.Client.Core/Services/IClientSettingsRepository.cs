using ModsDude.Client.Core.Persistence;

namespace ModsDude.Client.Core.Services;

public interface IClientSettingsRepository
{
    /// <inheritdoc cref="Store{T}.Read"/>
    T Read<T>(Func<ClientSettings, T> read);

    /// <summary>A copy of the settings as they are now, for a caller that reads more than one value.</summary>
    ClientSettings Snapshot();

    /// <inheritdoc cref="Store{T}.Update"/>
    void Update(Action<ClientSettings> update);
}
