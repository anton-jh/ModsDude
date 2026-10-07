using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Core.Mods;

/// <summary>
/// Every repo's registered mod versions, kept up to date from the server's feed of changes.
/// </summary>
public interface IModStore : IUserScopedState
{
    /// <summary>
    /// The repo's registered versions as the server has them now: reads whatever changed since the
    /// last read first, which is one small request where nothing did.
    /// </summary>
    /// <returns>Ordered by mod and version id.</returns>
    Task<IReadOnlyList<ModDto>> GetAsync(Guid repoId, CancellationToken cancellationToken);
}
