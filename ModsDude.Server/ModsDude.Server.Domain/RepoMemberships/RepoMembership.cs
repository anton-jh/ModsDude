using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.RepoMemberships;
public class RepoMembership(UserId userId, RepoId repoId, RepoMembershipLevel level)
{
    public UserId UserId { get; } = userId;
    public RepoId RepoId { get; } = repoId;
    public RepoMembershipLevel Level { get; set; } = level;
}
