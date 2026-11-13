using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Api.Endpoints.Mods;

/// <summary>
/// Deletes the files of mod versions whose deletion has committed.
/// </summary>
/// <remarks>
/// After the commit, never before it, and never fatal to it. A stranded file is recoverable: the next
/// import of the same version adopts it, and the reclamation sweep collects it otherwise. A
/// registration whose file is gone can never be repaired, because being registered is exactly what
/// stops an upload link from being minted for it again.
/// </remarks>
internal static class ModFileCleanup
{
    public static async Task DeleteAsync(
        IModStorageService storageService,
        ILogger logger,
        RepoId repoId,
        ModId modId,
        IEnumerable<ModVersionId> versionIds,
        CancellationToken cancellationToken)
    {
        foreach (var versionId in versionIds)
        {
            try
            {
                await storageService.DeleteMod(repoId, modId, versionId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception,
                    "The file of {Repo}/{Mod}/{Version} was not deleted after its registration was; the reclamation sweep will collect it.",
                    repoId.Value, modId.Value, versionId.Value);
            }
        }
    }
}
