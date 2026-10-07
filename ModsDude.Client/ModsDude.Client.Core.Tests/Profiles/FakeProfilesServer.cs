using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Tests.Profiles;

/// <summary>
/// The profile list and the writes that change it, answering the way the server does.
/// </summary>
internal sealed class FakeProfilesServer : IProfilesClient
{
    private TaskCompletionSource? _held;

    public List<ProfileDto> Profiles { get; } = [];

    public int Reads { get; private set; }

    /// <summary>Holds the next list read until the test releases it, answering with the list as it was when asked.</summary>
    public TaskCompletionSource Hold()
        => _held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);


    public async Task<ICollection<ProfileDto>> GetProfilesV1Async(Guid repoId, CancellationToken cancellationToken = default)
    {
        Reads++;

        // Copies, as a response would be: the store updates what it holds in place, and must not be
        // updating this list's entries along with it.
        ICollection<ProfileDto> response = [.. Profiles.Where(x => x.RepoId == repoId && x.ArchivedAt is null).Select(x => x with { })];

        if (_held is { } held)
        {
            _held = null;
            await held.Task.WaitAsync(cancellationToken);
        }

        return response;
    }

    public Task<ProfileDto> CreateProfileV1Async(Guid repoId, CreateProfileRequest request, CancellationToken cancellationToken = default)
    {
        var created = new ProfileDto { Id = Guid.NewGuid(), RepoId = repoId, Name = request.Name, HeadRevision = 1 };
        Profiles.Add(created);

        return Task.FromResult(created with { });
    }

    public Task<ProfileDto> UpdateProfileV1Async(Guid repoId, Guid profileId, UpdateProfileRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(Change(repoId, profileId, x => x.Name = request.Name));

    public Task<ProfileDto> ArchiveProfileV1Async(Guid repoId, Guid profileId, CancellationToken cancellationToken = default)
        => Task.FromResult(Change(repoId, profileId, x => x.ArchivedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)));

    public Task<ProfileDto> RestoreProfileV1Async(Guid repoId, Guid profileId, RestoreRequest? request = null, CancellationToken cancellationToken = default)
        => Task.FromResult(Change(repoId, profileId, x =>
        {
            x.ArchivedAt = null;
            x.Name = request?.Name ?? x.Name;
        }));

    public Task<ProfileRevisionDto> RestoreProfileRevisionV1Async(Guid repoId, Guid profileId, int number, RestoreProfileRevisionRequest? request = null, CancellationToken cancellationToken = default)
    {
        var restored = Change(repoId, profileId, x => x.HeadRevision++);

        return Task.FromResult(new ProfileRevisionDto { Number = restored.HeadRevision });
    }

    public Task<ProfileDto> GetProfileV1Async(Guid repoId, Guid profileId, CancellationToken cancellationToken = default)
        => Profiles.FirstOrDefault(x => x.RepoId == repoId && x.Id == profileId) is ProfileDto profile
            ? Task.FromResult(profile with { })
            : throw new ApiException<CustomProblemDetails>(
                "Not found", 400, null, new Dictionary<string, IEnumerable<string>>(), new CustomProblemDetails { Type = ProblemType.NotFound }, null);

    public Task DeleteProfileV1Async(Guid repoId, Guid profileId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ICollection<ProfileDto>> GetArchivedProfilesV1Async(Guid repoId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ProfileIgnoredModsDto> GetProfileIgnoredModsV1Async(Guid repoId, Guid profileId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ProfileIgnoredModsDto> SetProfileIgnoredModsV1Async(Guid repoId, Guid profileId, SetProfileIgnoredModsRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<GetProfileRevisionsResponse> GetProfileRevisionsV1Async(Guid repoId, Guid profileId, int? skip = null, int? limit = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ProfileRevisionDto> SaveProfileRevisionV1Async(Guid repoId, Guid profileId, SaveProfileRevisionRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<PruneProfileRevisionsResponse> PruneProfileRevisionsV1Async(Guid repoId, Guid profileId, PruneProfileRevisionsRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();


    private ProfileDto Change(Guid repoId, Guid profileId, Action<ProfileDto> change)
    {
        var profile = Profiles.Single(x => x.RepoId == repoId && x.Id == profileId);
        change(profile);

        return profile with { };
    }
}
