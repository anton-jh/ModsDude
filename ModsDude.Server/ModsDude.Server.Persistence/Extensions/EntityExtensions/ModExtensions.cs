using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Persistence.Extensions.EntityExtensions;
public static class ModExtensions
{
    public static object[] GetKey(this Mod mod)
    {
        return [mod.RepoId, mod.Id];
    }


    public static object[] GetKey(RepoId repoId, ModId modId)
    {
        return [repoId, modId];
    }
}
