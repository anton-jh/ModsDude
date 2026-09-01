using ModsDude.Server.Domain.Invites;
using ModsDude.Server.Domain.RepoMemberships;

namespace ModsDude.Server.Api.Dtos;

/// <summary>
/// <paramref name="Code"/> is the printable form, dashed into threes of four. It is what the client
/// shows and copies, and redemption accepts it back in whatever shape it arrives.
/// </summary>
public record RepoInviteDto(
    Guid Id,
    string Code,
    RepoMembershipLevel MembershipLevel,
    DateTime Created,
    DateTime? ExpiresAt,
    int? MaximumUses,
    int Uses,
    InviteStatus Status)
{
    public static RepoInviteDto FromModel(RepoInvite invite, DateTime now)
    {
        return new(
            invite.Id.Value,
            InviteCodes.Format(invite.Code),
            invite.GrantedLevel,
            invite.Created,
            invite.ExpiresAt,
            invite.MaximumUses,
            invite.Uses,
            invite.GetStatus(now));
    }
}
