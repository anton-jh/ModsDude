using ModsDude.Server.Domain.RepoMemberships;

namespace ModsDude.Server.Application.Authorization;

public abstract record AuthorizationResult
{
    public record InsufficientRepoAccess(RepoMembershipLevel? Current, RepoMembershipLevel Needed) : AuthorizationResult;
}
