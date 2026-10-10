using ModsDude.Server.Domain.RepoMemberships;

namespace ModsDude.Server.Application.Authorization;

public abstract record AuthorizationResult
{
    public record InsufficientRepoAccess(RepoMembershipLevel? Current, RepoMembershipLevel Needed) : AuthorizationResult;

    /// <summary>The user is not trusted. Unlike a membership level there is no threshold to report.</summary>
    public record NotTrusted : AuthorizationResult;
}
