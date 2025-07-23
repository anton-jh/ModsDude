using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Api.Dtos;

public record RepoDetailsDto(
    Guid Id,
    string Name,
    string AdapterId,
    string AdapterConfiguration,
    List<RepoMemberDto> Members)
    : RepoDto(Id, Name, AdapterId, AdapterConfiguration)
{
    public static RepoDetailsDto FromModel(Repo repo, IEnumerable<(User User, RepoMembership Membership)> members)
    {
        return new(
            repo.Id.Value,
            repo.Name.Value,
            repo.AdapterData.Id.Value,
            repo.AdapterData.Configuration.Value,
            members.Select(x => RepoMemberDto.FromModel(x.User, x.Membership)).ToList());
    }
}
