using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Services;

public interface IMembershipService
{
    Task<IReadOnlyList<RepoMemberDto>> GetMembers(Guid repoId, CancellationToken cancellationToken);

    Task UpdateMembership(Guid repoId, string userId, RepoMembershipLevel level, CancellationToken cancellationToken);

    Task KickMember(Guid repoId, string userId, CancellationToken cancellationToken);
}
