using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Application.Repositories;
public interface IModRepository
{
    Task<ModVersion?> GetModVersion(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken);
    Task<Mod?> GetMod(RepoId repoId, ModId modId, CancellationToken cancellationToken);
    void AddNewMod(Mod mod);
}
