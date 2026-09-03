using ModsDude.Server.Domain.Profiles;

namespace ModsDude.Server.Api.Dtos;

/// <summary>
/// <paramref name="HeadRevision"/> is the revision a read of the profile's mod list means when it
/// does not name one, and the number a save has to be based on. It is here so that a client holding
/// a profile knows which revision it is looking at without a second request.
/// </summary>
public record ProfileDto(Guid Id, Guid RepoId, string Name, int HeadRevision)
{
    public static ProfileDto FromModel(Profile profile)
        => new(profile.Id.Value, profile.RepoId.Value, profile.Name.Value, profile.HeadRevision.Value);
}
