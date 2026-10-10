using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Tests.Profiles;

/// <summary>
/// The profile list and the writes that change it, answering the way the server does.
/// </summary>
internal sealed class FakeProfilesServer : IProfilesClient
{
    private TaskCompletionSource? _held;
    private (Guid RequestId, int AnsweredWith)? _lastRevisionRequest;
    private readonly Dictionary<Guid, ProfileDto> _created = [];

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

    /// <summary>Answers a repeat of a request with the profile it made, as the server does.</summary>
    public Task<ProfileDto> CreateProfileV1Async(Guid repoId, CreateProfileRequest request, CancellationToken cancellationToken = default)
    {
        if (_created.TryGetValue(request.RequestId, out var repeat))
        {
            return Task.FromResult(repeat with { });
        }

        var created = new ProfileDto { Id = Guid.NewGuid(), RepoId = repoId, Name = request.Name, HeadRevision = 1 };
        Profiles.Add(created);
        _created[request.RequestId] = created;

        return Task.FromResult(created with { });
    }

    /// <summary>Refuses a rename made against another version, as the server does.</summary>
    public Task<ProfileDto> UpdateProfileV1Async(Guid repoId, Guid profileId, UpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        var profile = Profiles.Single(x => x.RepoId == repoId && x.Id == profileId);

        if (profile.Name != request.Name && profile.Version != request.ExpectedVersion)
        {
            throw new ApiException<CustomProblemDetails>(
                "Changed", 400, null, new Dictionary<string, IEnumerable<string>>(), new CustomProblemDetails { Type = ProblemType.ProfileChanged }, null);
        }

        return Task.FromResult(Change(repoId, profileId, x =>
        {
            x.Name = request.Name;
            x.Version++;
        }));
    }

    public Task<ProfileDto> ArchiveProfileV1Async(Guid repoId, Guid profileId, CancellationToken cancellationToken = default)
        => Task.FromResult(Change(repoId, profileId, x => x.ArchivedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)));

    public Task<ProfileDto> RestoreProfileV1Async(Guid repoId, Guid profileId, RestoreRequest? request = null, CancellationToken cancellationToken = default)
        => Task.FromResult(Change(repoId, profileId, x =>
        {
            x.ArchivedAt = null;
            x.Name = request?.Name ?? x.Name;
        }));

    /// <summary>Every revision restore the client sent, so a test can read what it based itself on.</summary>
    public List<RestoreProfileRevisionRequest> RevisionRestores { get; } = [];

    /// <summary>
    /// Answers a repeat of the latest restore as the original was, and refuses one based on another
    /// head, as the server does.
    /// </summary>
    public Task<ProfileRevisionDto> RestoreProfileRevisionV1Async(Guid repoId, Guid profileId, int number, RestoreProfileRevisionRequest? request = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        RevisionRestores.Add(request);

        if (_lastRevisionRequest is { } last && last.RequestId == request.RequestId)
        {
            return Task.FromResult(new ProfileRevisionDto { Number = last.AnsweredWith });
        }

        var profile = Profiles.Single(x => x.RepoId == repoId && x.Id == profileId);

        if (request.BasedOn != profile.HeadRevision)
        {
            throw new ApiException<CustomProblemDetails>(
                "Stale", 400, null, new Dictionary<string, IEnumerable<string>>(), new CustomProblemDetails { Type = ProblemType.ProfileRevisionStale }, null);
        }

        var restored = Change(repoId, profileId, x => x.HeadRevision++);

        _lastRevisionRequest = (request.RequestId, restored.HeadRevision);

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
