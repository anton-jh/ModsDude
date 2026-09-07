using ModsDude.Server.Domain.Profiles;

namespace ModsDude.Server.Api.Dtos;

/// <summary>
/// <paramref name="HeadRevision"/> is the revision a read of the profile's mod list means when it
/// does not name one, and the number a save has to be based on. It is here so that a client holding
/// a profile knows which revision it is looking at without a second request.
/// </summary>
/// <param name="ArchivedAt">
/// When it was put away, or null while it is live. Carried on the DTO rather than left implicit in
/// which list it arrived in, because several archived profiles may share a name and this is the only
/// thing telling them apart.
/// </param>
public record ProfileDto(Guid Id, Guid RepoId, string Name, int HeadRevision, DateTime? ArchivedAt)
{
    public static ProfileDto FromModel(Profile profile)
        => new(profile.Id.Value, profile.RepoId.Value, profile.Name.Value, profile.HeadRevision.Value, profile.ArchivedAt);
}
