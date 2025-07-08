using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Application.Dependencies;
public interface IModStorageService
{
    Task<bool> CheckIfModExists(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken);
    Task<string> GetUploadLink(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken);
}
