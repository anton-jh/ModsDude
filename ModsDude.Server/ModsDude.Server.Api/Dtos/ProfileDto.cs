using ModsDude.Server.Domain.Profiles;

namespace ModsDude.Server.Api.Dtos;

public record ProfileDto(Guid Id, Guid RepoId, string Name)
{
    public static ProfileDto FromModel(Profile profile)
        => new(profile.Id.Value, profile.RepoId.Value, profile.Name.Value);
}
