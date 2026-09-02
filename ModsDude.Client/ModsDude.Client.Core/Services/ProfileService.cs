using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Core.Services;
public class ProfileService(
    IProfilesClient profileClient,
    IModDependenciesClient modDependencyClient,
    IModsClient modsClient)
    : IUserScopedState
{
    /// <summary>Only ever walked to the end, so the page size is a round-trip count, not a UI concern.</summary>
    private const int _modPageSize = 200;

    public delegate void ProfileCreatedEventHandler(Guid profileId);
    public delegate void ProfileUpdatedEventHandler(Guid profileId);

    /// <summary>Raised for a profile that did not exist a moment ago, so the shell can navigate to it.</summary>
    public event ProfileCreatedEventHandler? ProfileCreated;

    /// <summary>
    /// Raised when an existing profile's contents changed. The <see cref="ProfileDto"/> instance in
    /// <see cref="Profiles"/> is updated in place rather than replaced - replacing it would take the
    /// sidebar entry, and the selection on it, down with it - and the DTO cannot announce that
    /// itself.
    /// </summary>
    public event ProfileUpdatedEventHandler? ProfileUpdated;

    public ObservableCollection<ProfileDto> Profiles { get; } = [];


    public async Task RefreshProfiles(Guid repoId, CancellationToken cancellationToken)
    {
        var profiles = await profileClient.GetProfilesV1Async(repoId, cancellationToken);

        var byId = profiles.ToDictionary(x => x.Id);

        // Profile ids are unique across repos, so this also handles the collection being handed over
        // to a different repo: nothing matches, everything is swapped.
        for (var i = Profiles.Count - 1; i >= 0; i--)
        {
            if (!byId.ContainsKey(Profiles[i].Id))
            {
                Profiles.RemoveAt(i);
            }
        }

        foreach (var dto in profiles)
        {
            if (FindProfile(dto.Id) is ProfileDto existing)
            {
                Apply(existing, dto);
            }
            else
            {
                Profiles.Add(dto);
            }
        }
    }

    /// <summary>
    /// The collection is handed from repo to repo as the user navigates, so on a user change it is
    /// simply handed to nobody.
    /// </summary>
    public void ClearUserState()
    {
        Profiles.Clear();
    }

    public async Task CreateProfile(Guid repoId, string name, CancellationToken cancellationToken)
    {
        var request = new CreateProfileRequest()
        {
            Name = name
        };

        ProfileDto profile;

        try
        {
            profile = await profileClient.CreateProfileV1Async(repoId, request, cancellationToken);
        }
        catch (ApiException<CustomProblemDetails> ex) when (ex.Result.Type == ProblemType.NameTaken)
        {
            throw new UserFriendlyException("Name taken", null, ex);
        }

        Profiles.Add(profile);

        ProfileCreated?.Invoke(profile.Id);
    }

    public async Task UpdateProfile(Guid repoId, Guid profileId, string name, CancellationToken cancellationToken)
    {
        var request = new UpdateProfileRequest()
        {
            Name = name
        };

        ProfileDto updated;

        try
        {
            updated = await profileClient.UpdateProfileV1Async(repoId, profileId, request, cancellationToken);
        }
        catch (ApiException<CustomProblemDetails> ex) when (ex.Result.Type == ProblemType.NameTaken)
        {
            throw new UserFriendlyException("Name taken", null, ex);
        }

        if (FindProfile(profileId) is ProfileDto existing)
        {
            Apply(existing, updated);
        }
    }

    public async Task DeleteProfile(Guid repoId, Guid profileId, CancellationToken cancellationToken)
    {
        await profileClient.DeleteProfileV1Async(repoId, profileId, cancellationToken);

        if (FindProfile(profileId) is ProfileDto removed)
        {
            Profiles.Remove(removed);
        }
    }


    /// <summary>How many mods the profile pins. Not held in <see cref="Profiles"/>: the DTO does not carry it.</summary>
    public async Task<int> GetModCount(Guid repoId, Guid profileId, CancellationToken cancellationToken)
    {
        var dependencies = await modDependencyClient.GetModDependenciesV1Async(repoId, profileId, cancellationToken);

        return dependencies.Count;
    }

    /// <summary>
    /// What the profile pins, with each version resolved to the registered record behind it - which
    /// is what the shared list row needs to render one.
    /// </summary>
    /// <remarks>
    /// Two reads and a join, and deliberately not a <c>ModCatalog</c>: the catalog exists to merge
    /// the repo's mods with what is on this machine's disks, and a reader who cannot edit the profile
    /// has no use for the local half and should not pay a scan for it. Both routes are readable at
    /// Guest, which is the level this is for.
    /// </remarks>
    public async Task<IReadOnlyList<PinnedMod>> GetPinnedMods(Guid repoId, Guid profileId, CancellationToken cancellationToken)
    {
        var dependencies = await modDependencyClient.GetModDependenciesV1Async(repoId, profileId, cancellationToken);

        if (dependencies.Count == 0)
        {
            return [];
        }

        var registered = await GetRegisteredVersions(repoId, cancellationToken);

        return
        [
            .. dependencies
                .Select(dependency =>
                {
                    var modId = ModKey.From(dependency.ModId);
                    var versionId = ModVersionKey.From(dependency.ModVersionId);

                    // The adapter's flag lives on the version, the user's on the dependency.
                    return registered.TryGetValue((modId, versionId), out var version)
                        ? new PinnedMod(
                            CatalogModVersion.FromRegistered(version),
                            new ProfileModLock(version.Locked, dependency.Locked))
                        : null;
                })
                // A miss cannot mean "pinned at a version the repo lost": the dependency's foreign
                // key onto ModVersions is required and Restrict, so that version could not have been
                // deleted while this dependency named it. What it does mean is that these are two
                // reads and the mod list is the later one - somebody unpinned the mod and then
                // deleted the version in between. The pin is gone, so the row is too, which is what
                // a refresh would show anyway.
                .OfType<PinnedMod>()
                .OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        ];
    }

    private async Task<Dictionary<(ModKey, ModVersionKey), ModDto>> GetRegisteredVersions(
        Guid repoId, CancellationToken cancellationToken)
    {
        var byKey = new Dictionary<(ModKey, ModVersionKey), ModDto>();
        string? cursor = null;

        do
        {
            var page = await modsClient.GetModsV1Async(repoId, null, cursor, _modPageSize, cancellationToken);

            foreach (var mod in page.Mods)
            {
                byKey[(ModKey.From(mod.ModId), ModVersionKey.From(mod.VersionId))] = mod;
            }

            cursor = page.NextCursor;
        }
        while (string.IsNullOrEmpty(cursor) is false);

        return byKey;
    }


    private ProfileDto? FindProfile(Guid id)
    {
        return Profiles.FirstOrDefault(x => x.Id == id);
    }

    private void Apply(ProfileDto target, ProfileDto source)
    {
        if (target.Name == source.Name)
        {
            return;
        }

        target.Name = source.Name;

        ProfileUpdated?.Invoke(target.Id);
    }
}
