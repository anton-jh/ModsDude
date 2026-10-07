using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Stores;
using System.Collections.Frozen;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Core.Profiles;

public sealed class ProfileStore(
    IProfilesClient profilesClient,
    IStoreDispatcher dispatcher,
    ILogger<ProfileStore> logger)
    : IProfileStore
{
    private readonly StoreLoads<Guid> _loads = new(dispatcher, logger);
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, ObservableCollection<Profile>> _live = [];
    private readonly HashSet<Guid> _loaded = [];

    /// <summary>
    /// Every live profile by repo and id, replaced whole on every change, because <see cref="Find"/>
    /// is asked off the store's thread while the collections are not safe to read there.
    /// </summary>
    private volatile FrozenDictionary<(Guid RepoId, Guid ProfileId), Profile> _byId
        = FrozenDictionary<(Guid, Guid), Profile>.Empty;


    public event Action<Profile>? ProfileCreated;

    public event Action<Guid>? Changed;


    public IReadOnlyList<Guid> LoadedRepos
    {
        get
        {
            lock (_lock)
            {
                return [.. _loaded.Order()];
            }
        }
    }


    public ObservableCollection<Profile> Live(Guid repoId)
    {
        lock (_lock)
        {
            if (_live.TryGetValue(repoId, out var live) is false)
            {
                live = [];
                _live[repoId] = live;
            }

            return live;
        }
    }

    public bool IsLoaded(Guid repoId)
    {
        lock (_lock)
        {
            return _loaded.Contains(repoId);
        }
    }

    public Profile? Find(Guid repoId, Guid? profileId)
        => profileId is Guid id ? _byId.GetValueOrDefault((repoId, id)) : null;

    public int? GetHeadRevision(ActiveProfile profile)
        => Find(profile.RepoId, profile.ProfileId)?.HeadRevision;

    public Task EnsureLoadedAsync(Guid repoId, CancellationToken cancellationToken)
        => IsLoaded(repoId) ? Task.CompletedTask : RefreshAsync(repoId, cancellationToken);

    public Task RefreshAsync(Guid repoId, CancellationToken cancellationToken)
        => _loads.ReadAsync(
            repoId,
            ct => profilesClient.GetProfilesV1Async(repoId, ct),
            profiles => Reconcile(repoId, profiles),
            cancellationToken);

    public async Task<Profile> CreateAsync(
        Guid repoId,
        string name,
        CopyProfileRevisionRequest? copyFrom,
        CancellationToken cancellationToken)
    {
        var request = new CreateProfileRequest { Name = name, CopyFrom = copyFrom };

        var created = await _loads.WriteAsync(
            repoId,
            ct => NameTaken(() => profilesClient.CreateProfileV1Async(repoId, request, ct)),
            UpsertCreated,
            cancellationToken);

        return Held(created);
    }

    public async Task RenameAsync(Profile profile, string name, CancellationToken cancellationToken)
    {
        var request = new UpdateProfileRequest { Name = name, ExpectedVersion = profile.Version };

        try
        {
            await _loads.WriteAsync(
                profile.RepoId,
                ct => NameTaken(() => profilesClient.UpdateProfileV1Async(profile.RepoId, profile.Id, request, ct)),
                dto => Upsert(dto),
                cancellationToken);
        }
        catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type is ProblemType.ProfileChanged)
        {
            await RefreshAfterRefusalAsync(profile.RepoId, "a rename", cancellationToken);

            throw new UserFriendlyException("Somebody else changed this profile", "Look at it again and rename it again.", exception);
        }
    }

    /// <summary>
    /// So that looking again shows what somebody else changed. Not reading it is no reason to hide
    /// why the write was refused, so a failed read is only logged.
    /// </summary>
    private async Task RefreshAfterRefusalAsync(Guid repoId, string refused, CancellationToken cancellationToken)
    {
        try
        {
            await RefreshAsync(repoId, cancellationToken);
        }
        catch (Exception refresh) when (refresh is not OperationCanceledException)
        {
            logger.LogWarning(refresh, "Could not read the profiles of repo {Repo} again after {Refused} was refused.", repoId, refused);
        }
    }

    public Task ArchiveAsync(Profile profile, CancellationToken cancellationToken)
        => _loads.WriteAsync(
            profile.RepoId,
            ct => profilesClient.ArchiveProfileV1Async(profile.RepoId, profile.Id, ct),
            dto => Remove(dto.RepoId, dto.Id),
            cancellationToken);

    public async Task<Profile> RestoreAsync(Guid repoId, Guid profileId, string? name, CancellationToken cancellationToken)
    {
        var restored = await _loads.WriteAsync(
            repoId,
            ct => NameTaken(() => profilesClient.RestoreProfileV1Async(repoId, profileId, new RestoreRequest { Name = name }, ct)),
            UpsertCreated,
            cancellationToken);

        return Held(restored);
    }

    public Task DeleteAsync(Guid repoId, Guid profileId, CancellationToken cancellationToken)
        => _loads.WriteAsync(
            repoId,
            ct => profilesClient.DeleteProfileV1Async(repoId, profileId, ct),
            () => Remove(repoId, profileId),
            cancellationToken);

    public async Task<ProfileRevisionDto> RestoreRevisionAsync(Profile profile, int number, int basedOn, CancellationToken cancellationToken)
    {
        var request = new RestoreProfileRevisionRequest { RequestId = Guid.NewGuid(), BasedOn = basedOn };

        try
        {
            return await _loads.WriteAsync(
                profile.RepoId,
                ct => profilesClient.RestoreProfileRevisionV1Async(profile.RepoId, profile.Id, number, request, ct),
                revision => ApplyHeadRevision(profile.RepoId, profile.Id, revision.Number),
                cancellationToken);
        }
        catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type is ProblemType.ProfileRevisionStale)
        {
            await RefreshAfterRefusalAsync(profile.RepoId, "a revision restore", cancellationToken);

            throw new UserFriendlyException("Somebody else saved this profile", "Look at its history again and restore again.", exception);
        }
    }

    public Task ApplyRevisionSavedAsync(Guid repoId, Guid profileId, int number)
        => _loads.ApplyAsync(repoId, () => ApplyHeadRevision(repoId, profileId, number));

    public void ClearUserState()
    {
        _loads.Reset();

        List<(Guid RepoId, ObservableCollection<Profile> Live)> cleared;

        lock (_lock)
        {
            cleared = [.. _live.Select(x => (x.Key, x.Value))];
            _loaded.Clear();
        }

        foreach (var (_, live) in cleared)
        {
            live.Clear();
        }

        Publish();

        foreach (var (repoId, _) in cleared)
        {
            Changed?.Invoke(repoId);
        }
    }


    private void Reconcile(Guid repoId, ICollection<ProfileDto> profiles)
    {
        var live = Live(repoId);
        var byId = profiles.ToDictionary(x => x.Id);
        var changed = false;

        for (var i = live.Count - 1; i >= 0; i--)
        {
            if (byId.ContainsKey(live[i].Id) is false)
            {
                live.RemoveAt(i);
                changed = true;
            }
        }

        foreach (var dto in profiles.OrderBy(x => x.Id))
        {
            changed |= Put(live, dto);
        }

        lock (_lock)
        {
            changed |= _loaded.Add(repoId);
        }

        if (changed)
        {
            Publish();
            Changed?.Invoke(repoId);
        }
    }

    /// <summary>Puts a profile the server just answered with into its repo's list, or updates the one there.</summary>
    /// <remarks>An archived one is taken out instead: the live list holds live profiles only.</remarks>
    private void Upsert(ProfileDto dto)
    {
        if (dto.ArchivedAt is not null)
        {
            Remove(dto.RepoId, dto.Id);

            return;
        }

        if (Put(Live(dto.RepoId), dto))
        {
            Publish();
            Changed?.Invoke(dto.RepoId);
        }
    }

    /// <returns>Whether the list or the profile changed.</returns>
    private static bool Put(ObservableCollection<Profile> live, ProfileDto dto)
    {
        if (live.FirstOrDefault(x => x.Id == dto.Id) is Profile existing)
        {
            return existing.Apply(dto);
        }

        live.Add(new Profile(dto));

        return true;
    }

    private void UpsertCreated(ProfileDto dto)
    {
        Upsert(dto);
        ProfileCreated?.Invoke(Held(dto));
    }

    private void Remove(Guid repoId, Guid profileId)
    {
        var live = Live(repoId);

        if (live.FirstOrDefault(x => x.Id == profileId) is not Profile held)
        {
            return;
        }

        live.Remove(held);

        Publish();
        Changed?.Invoke(repoId);
    }

    private void ApplyHeadRevision(Guid repoId, Guid profileId, int number)
    {
        if (Find(repoId, profileId) is Profile profile && profile.ApplyHeadRevision(number))
        {
            Changed?.Invoke(repoId);
        }
    }

    private void Publish()
    {
        lock (_lock)
        {
            _byId = _live
                .SelectMany(repo => repo.Value.Select(profile => KeyValuePair.Create((repo.Key, profile.Id), profile)))
                .ToFrozenDictionary();
        }
    }

    /// <summary>The model a write just put in the list, which is gone only where the user changed meanwhile.</summary>
    private Profile Held(ProfileDto dto)
        => Find(dto.RepoId, dto.Id) ?? throw new OperationCanceledException("The user changed while the profile was being written.");

    private static async Task<T> NameTaken<T>(Func<Task<T>> send)
    {
        try
        {
            return await send();
        }
        catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type == ProblemType.NameTaken)
        {
            throw new UserFriendlyException("Name taken", null, exception);
        }
    }
}
