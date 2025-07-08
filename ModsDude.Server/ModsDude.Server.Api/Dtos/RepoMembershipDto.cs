using ModsDude.Server.Domain.RepoMemberships;

namespace ModsDude.Server.Api.Dtos;

public record RepoMembershipDto(RepoDto Repo, RepoMembershipLevel MembershipLevel);
