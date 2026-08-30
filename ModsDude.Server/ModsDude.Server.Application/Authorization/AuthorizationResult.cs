using ModsDude.Server.Domain.RepoMemberships;

namespace ModsDude.Server.Application.Authorization;

public abstract record AuthorizationResult
{
    public record InsufficientRepoAccess(RepoMembershipLevel? Current, RepoMembershipLevel Needed) : AuthorizationResult;

    /// <summary>
    /// The user does not carry the manually granted trust flag. Deliberately carries nothing: unlike
    /// a membership level there is no threshold to report and nothing the user can do about it in the
    /// app, so the refusal says only that it was refused.
    /// </summary>
    public record NotTrusted : AuthorizationResult;
}
