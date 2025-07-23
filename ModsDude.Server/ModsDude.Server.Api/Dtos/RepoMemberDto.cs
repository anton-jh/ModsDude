using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Api.Dtos;

public record RepoMemberDto(UserDto User, RepoMembershipLevel MembershipLevel)
{
    public static RepoMemberDto FromModel(User user, RepoMembership membership)
    {
        return new(UserDto.FromModel(user), membership.Level);
    }
}
