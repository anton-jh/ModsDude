using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Sync;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Core.Services;
public class ProfileService(
    IProfilesClient profileClient,
    IModDependenciesClient modDependencyClient,
    IModsClient modsClient)
    : IUserScopedState, IProfileRevisions
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

    /// <summary>
    /// What the drift check asks so it can say "this folder is on revision 6, the profile is at 8".
    /// </summary>
    /// <remarks>
    /// Answered from <see cref="Profiles"/>, which holds one repo at a time, so this is null for
    /// every profile outside the repo the user is standing in - and null on purpose. Going and
    /// fetching it would put a network round trip per instance into a check that runs on every
    /// window activation and is meant to work offline.
    /// </remarks>
    public int? GetHeadRevision(ActiveProfile profile)
    {
        var known = FindProfile(profile.ProfileId);

        return known is not null && known.RepoId == profile.RepoId ? known.HeadRevision : null;
    }

    /// <param name="copyFrom">
    /// A revision of another profile in the repo to branch off, or <c>null</c> for an empty profile.
    /// The new profile's first revision pins exactly what that one pinned.
    /// </param>
    public async Task CreateProfile(
        Guid repoId,
        string name,
        CopyProfileRevisionRequest? copyFrom = null,
        CancellationToken cancellationToken = default)
    {
        var request = new CreateProfileRequest()
        {
            Name = name,
            CopyFrom = copyFrom
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
        var response = await modDependencyClient.GetModDependenciesV1Async(repoId, profileId, null, cancellationToken);

        return response.Dependencies.Count;
    }

    /// <summary>
    /// The profile's history, newest first, with the number of the revision that is current.
    /// </summary>
    public async Task<ProfileHistory> GetHistory(Guid repoId, Guid profileId, CancellationToken cancellationToken)
    {
        var response = await profileClient.GetProfileRevisionsV1Async(repoId, profileId, null, null, cancellationToken);

        return new ProfileHistory([.. response.Revisions], response.HeadRevision, response.HasMore);
    }

    /// <summary>
    /// Puts an older revision's mod list back by copying it to the front. Nothing is deleted, so the
    /// revisions in between stay readable and this is itself undoable.
    /// </summary>
    public async Task<ProfileRevisionDto> RestoreRevision(Guid repoId, Guid profileId, int number, CancellationToken cancellationToken)
    {
        var restored = await profileClient.RestoreProfileRevisionV1Async(
            repoId, profileId, number, new RestoreProfileRevisionRequest(), cancellationToken);

        if (FindProfile(profileId) is ProfileDto existing)
        {
            existing.HeadRevision = restored.Number;

            ProfileUpdated?.Invoke(profileId);
        }

        return restored;
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
    /// <param name="revision">
    /// Which revision to read, or <c>null</c> for the profile's current one. An older revision is
    /// the same list rendered the same way - it is only read-only because nothing anywhere can write
    /// to one.
    /// </param>
    public async Task<IReadOnlyList<PinnedMod>> GetPinnedMods(Guid repoId, Guid profileId, int? revision, CancellationToken cancellationToken)
    {
        var response = await modDependencyClient.GetModDependenciesV1Async(repoId, profileId, revision, cancellationToken);
        var dependencies = response.Dependencies;

        if (dependencies.Count == 0)
        {
            return [];
        }

        var registered = await GetRegisteredVersions(repoId, cancellationToken);

        return Resolve(dependencies, registered);
    }

    /// <summary>
    /// What changed between two revisions of a profile, mod by mod.
    /// </summary>
    /// <remarks>
    /// Two dependency reads and <b>one</b> walk of the registered mod list, which is why this is a
    /// method rather than two calls to <see cref="GetPinnedMods"/>: the catalog walk is the
    /// expensive half, and doing it twice to compare two lists of the same repo's mods would be
    /// paying for the same answer again.
    /// </remarks>
    public async Task<ProfileRevisionComparison> CompareRevisions(
        Guid repoId, Guid profileId, int from, int to, CancellationToken cancellationToken)
    {
        var before = await modDependencyClient.GetModDependenciesV1Async(repoId, profileId, from, cancellationToken);
        var after = await modDependencyClient.GetModDependenciesV1Async(repoId, profileId, to, cancellationToken);

        if (before.Dependencies.Count == 0 && after.Dependencies.Count == 0)
        {
            return new ProfileRevisionComparison(from, to, []);
        }

        var registered = await GetRegisteredVersions(repoId, cancellationToken);

        return ProfileRevisionComparison.Between(
            from,
            to,
            Resolve(before.Dependencies, registered),
            Resolve(after.Dependencies, registered));
    }

    private static IReadOnlyList<PinnedMod> Resolve(
        ICollection<ModDependencyDto> dependencies,
        Dictionary<(ModKey, ModVersionKey), ModDto> registered)
    {
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
                .OrderBy(x => x.DisplayName, NaturalOrder.Comparer)
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
        if (target.Name == source.Name && target.HeadRevision == source.HeadRevision)
        {
            return;
        }

        target.Name = source.Name;

        // Kept in step so that a page holding this DTO saves against the revision the server is
        // actually on. A save based on a stale number is refused, which is the right answer - but
        // being refused for a number this client could have refreshed is not.
        target.HeadRevision = source.HeadRevision;

        ProfileUpdated?.Invoke(target.Id);
    }
}


/// <param name="HasMore">
/// Whether older revisions were left unread. The listing is windowed from the newest, and nothing
/// yet asks for a second page - see docs/PLAN.md.
/// </param>
public sealed record ProfileHistory(IReadOnlyList<ProfileRevisionDto> Revisions, int HeadRevision, bool HasMore);
