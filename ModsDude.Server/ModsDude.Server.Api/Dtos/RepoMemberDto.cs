using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Api.Dtos;

/// <param name="IsBlocked">
/// A blocked member keeps their membership but cannot act on it, so a blocked admin does not count
/// towards a repo having another admin.
/// </param>
public record RepoMemberDto(UserDto User, RepoMembershipLevel MembershipLevel, bool IsBlocked)
{
    public static RepoMemberDto FromModel(User user, RepoMembership membership)
    {
        return new(UserDto.FromModel(user), membership.Level, user.IsBlocked);
    }
}
