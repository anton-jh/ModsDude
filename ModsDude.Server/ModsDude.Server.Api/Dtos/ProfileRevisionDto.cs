using ModsDude.Server.Domain.Profiles;

namespace ModsDude.Server.Api.Dtos;

/// <summary>
/// One entry in a profile's history.
/// </summary>
/// <remarks>
/// It carries no mod list. A revision's contents are read through
/// <c>GET repos/{repoId}/profiles/{profileId}/modDependencies?revision=N</c>, because a history page
/// renders tens of these and a profile holds one to two thousand mods.
/// </remarks>
/// <param name="SourceRevision">
/// Where this revision's contents came from, for a restore or a profile branched off another one.
/// It is what lets the history say "restored from 3" rather than showing a rollback as an ordinary
/// edit that happens to match an old one.
/// </param>
public record ProfileRevisionDto(
    Guid RepoId,
    Guid ProfileId,
    int Number,
    DateTime Created,
    UserDto CreatedBy,
    string? Label,
    ProfileRevisionOrigin Origin,
    Guid? SourceProfileId,
    int? SourceRevision,
    int ModCount,
    ProfileRevisionChangesDto Changes)
{
    public static ProfileRevisionDto FromModel(ProfileRevision revision, UserDto createdBy)
        => new(
            revision.RepoId.Value,
            revision.ProfileId.Value,
            revision.Number.Value,
            revision.Created,
            createdBy,
            revision.Label,
            revision.Origin,
            revision.SourceProfileId?.Value,
            revision.SourceRevision?.Value,
            revision.ModCount,
            new ProfileRevisionChangesDto(revision.Changes.Added, revision.Changes.Changed, revision.Changes.Removed));
}

/// <summary>What a revision did to the one before it.</summary>
public record ProfileRevisionChangesDto(int Added, int Changed, int Removed);
