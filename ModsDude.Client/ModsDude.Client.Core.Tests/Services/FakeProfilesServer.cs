using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Tests.Services;

/// <summary>
/// The profile list and nothing else: what a refresh and a background check both read.
/// </summary>
internal sealed class FakeProfilesServer : IProfilesClient
{
    public List<ProfileDto> Profiles { get; } = [];

    public int Reads { get; private set; }

    /// <summary>Runs after the list is read and before it is returned - the request being out.</summary>
    public Action? DuringRead { get; set; }


    public Task<ICollection<ProfileDto>> GetProfilesV1Async(Guid repoId, CancellationToken cancellationToken = default)
    {
        Reads++;

        // Copies, as a response would be: the service updates what it holds in place, and must not be
        // updating this list's entries along with it.
        ICollection<ProfileDto> response = [.. Profiles.Where(x => x.RepoId == repoId).Select(x => x with { })];

        DuringRead?.Invoke();

        return Task.FromResult(response);
    }


    public Task<ProfileDto> ArchiveProfileV1Async(Guid repoId, Guid profileId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ProfileDto> CreateProfileV1Async(Guid repoId, CreateProfileRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task DeleteProfileV1Async(Guid repoId, Guid profileId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ProfileDto> UpdateProfileV1Async(Guid repoId, Guid profileId, UpdateProfileRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ICollection<ProfileDto>> GetArchivedProfilesV1Async(Guid repoId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ProfileIgnoredModsDto> GetProfileIgnoredModsV1Async(Guid repoId, Guid profileId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ProfileIgnoredModsDto> SetProfileIgnoredModsV1Async(Guid repoId, Guid profileId, SetProfileIgnoredModsRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<GetProfileRevisionsResponse> GetProfileRevisionsV1Async(Guid repoId, Guid profileId, int? skip = null, int? limit = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ProfileRevisionDto> SaveProfileRevisionV1Async(Guid repoId, Guid profileId, SaveProfileRevisionRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ProfileDto> GetProfileV1Async(Guid repoId, Guid profileId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<PruneProfileRevisionsResponse> PruneProfileRevisionsV1Async(Guid repoId, Guid profileId, PruneProfileRevisionsRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ProfileRevisionDto> RestoreProfileRevisionV1Async(Guid repoId, Guid profileId, int number, RestoreProfileRevisionRequest? request = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ProfileDto> RestoreProfileV1Async(Guid repoId, Guid profileId, RestoreRequest? request = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
}
