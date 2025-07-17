using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Models;
public class ProfileDtoWithRepo(RepoMembershipDto repoMembership, ProfileDto profile)
{
    public RepoMembershipDto RepoMembership { get; } = repoMembership;
    public ProfileDto Profile { get; } = profile;
}
