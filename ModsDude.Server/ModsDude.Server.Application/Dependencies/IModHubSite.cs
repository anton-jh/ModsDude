using ModsDude.Server.Domain.ModHub;

namespace ModsDude.Server.Application.Dependencies;

/// <summary>
/// Reads the public ModHub website. Paced: every call waits its turn, so a caller can loop over
/// thousands of pages without thinking about how hard it is hitting somebody else's server.
/// </summary>
/// <remarks>
/// Throws <see cref="ModHubUnavailableException"/> when the site does not answer normally, and
/// <see cref="ModHubUnreadableException"/> when a page no longer looks the way the parser expects -
/// both mean "stop this run", and neither may be read as "there is nothing there".
/// </remarks>
public interface IModHubSite
{
    /// <summary>
    /// The mod ids on one page of the "latest" listing, in order, featured mods excluded. Empty past
    /// the last page.
    /// </summary>
    Task<IReadOnlyList<int>> GetLatestPage(string game, int page, CancellationToken cancellationToken);

    /// <summary>A mod's page, or null where ModHub says it has no such mod.</summary>
    Task<ModHubModDetails?> GetMod(string game, int modHubId, CancellationToken cancellationToken);

    /// <summary>The page a person opens to see and download the mod.</summary>
    string GetModPageUrl(string game, int modHubId);
}

public class ModHubUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public class ModHubUnreadableException(string message)
    : Exception(message);
