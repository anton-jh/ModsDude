using ModsDude.Server.Application.Repositories;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Persistence.Repositories;
public class ModRepository(
    ApplicationDbContext dbContext)
    : IModRepository
{
    public void AddNewMod(Mod mod)
    {
        dbContext.Mods.Add(mod);
    }

    public async Task<Mod?> GetMod(RepoId repoId, ModId modId, CancellationToken cancellationToken)
    {
        return await dbContext.Mods.FindAsync([repoId, modId], cancellationToken);
    }

    public async Task<ModVersion?> GetModVersion(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken)
    {
        var mod = await dbContext.Mods.FindAsync([repoId, modId], cancellationToken);

        return mod?.Versions.FirstOrDefault(x => x.Id == versionId);
    }
}
