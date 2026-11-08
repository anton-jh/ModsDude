using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.RepoMemberships;
public class RepoMembership(UserId userId, RepoId repoId, RepoMembershipLevel level)
{
    public UserId UserId { get; } = userId;
    public RepoId RepoId { get; } = repoId;
    public RepoMembershipLevel Level { get; internal set; } = level;

    /// <summary>
    /// The member, where whoever loaded the repo loaded it too. Only what decides whether an admin can
    /// still act - <see cref="Repo.IsOnlyAdmin"/> - needs it.
    /// </summary>
    public User? User { get; internal set; }
}
