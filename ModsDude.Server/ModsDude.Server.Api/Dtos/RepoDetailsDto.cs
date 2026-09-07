using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Api.Dtos;

public record RepoDetailsDto(
    Guid Id,
    string Name,
    string Tag,
    string AdapterId,
    string AdapterConfiguration,
    List<RepoMemberDto> Members,
    DateTime? ArchivedAt)
    : RepoDto(Id, Name, Tag, AdapterId, AdapterConfiguration, ArchivedAt)
{
    public static RepoDetailsDto FromModel(Repo repo, IEnumerable<(User User, RepoMembership Membership)> members)
    {
        return new(
            repo.Id.Value,
            repo.Name.Value,
            RepoTag.For(repo.Id),
            repo.AdapterData.Id.Value,
            repo.AdapterData.Configuration.Value,
            members.Select(x => RepoMemberDto.FromModel(x.User, x.Membership)).ToList(),
            repo.ArchivedAt);
    }
}
