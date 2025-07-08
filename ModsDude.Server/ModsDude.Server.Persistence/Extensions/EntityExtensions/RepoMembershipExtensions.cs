using ModsDude.Server.Domain.RepoMemberships;

namespace ModsDude.Server.Persistence.Extensions.EntityExtensions;

public static class RepoMembershipExtensions
{
    public static object[] GetKey(this RepoMembership repoMembership)
    {
        return [repoMembership.UserId, repoMembership.RepoId];
    }
}
