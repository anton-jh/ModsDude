using ModsDude.Server.Domain.Invites;
using ModsDude.Server.Domain.RepoMemberships;

namespace ModsDude.Server.Api.Dtos;

/// <summary>
/// An invite as the repo's list shows it. The code is not on it: only the person who made an invite
/// may read its code again, from its own endpoint.
/// </summary>
/// <param name="CreatedBy">Who made it, or null where an operator made it on the admin pages.</param>
public record RepoInviteDto(
    Guid Id,
    RepoMembershipLevel MembershipLevel,
    DateTime Created,
    DateTime? ExpiresAt,
    int? MaximumUses,
    int Uses,
    InviteStatus Status,
    UserDto? CreatedBy)
{
    /// <param name="inviterLevel">See <see cref="RepoInvite.GetStatus"/>.</param>
    public static RepoInviteDto FromModel(RepoInvite invite, DateTime now, RepoMembershipLevel? inviterLevel, UserDto? createdBy)
    {
        return new(
            invite.Id.Value,
            invite.GrantedLevel,
            invite.Created,
            invite.ExpiresAt,
            invite.MaximumUses,
            invite.Uses,
            invite.GetStatus(now, inviterLevel),
            createdBy);
    }
}


/// <summary>The printable form, dashed into threes of four. Redemption accepts it back in whatever shape it arrives.</summary>
public record InviteCodeDto(string Code);
