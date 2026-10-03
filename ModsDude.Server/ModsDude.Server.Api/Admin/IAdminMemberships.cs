using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Api.Admin;

/// <summary>
/// Membership changes made by the operator, from either the repos or the users page. Level rules
/// do not apply to the operator; the repo's own invariants do. Each returns why it was refused, or
/// null once the membership is as asked, including when it already was.
/// </summary>
public interface IAdminMemberships
{
    Task<string?> AddAsync(RepoId repoId, UserId userId, RepoMembershipLevel level, string operatorName, CancellationToken cancellationToken);

    /// <param name="expectedRevision">The repo's membership revision the form was drawn from.</param>
    Task<string?> SetLevelAsync(RepoId repoId, UserId userId, RepoMembershipLevel level, int expectedRevision, string operatorName, CancellationToken cancellationToken);

    /// <param name="expectedRevision">The repo's membership revision the form was drawn from.</param>
    Task<string?> RemoveAsync(RepoId repoId, UserId userId, int expectedRevision, string operatorName, CancellationToken cancellationToken);
}
