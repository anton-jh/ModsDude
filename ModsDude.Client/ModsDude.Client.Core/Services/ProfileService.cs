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
    : IProfileService
{
    /// <summary>Only ever walked to the end, so the page size is a round-trip count, not a UI concern.</summary>
    private const int _modPageSize = 200;

    public event Action<Guid>? ProfileCreated;

    public event Action<Guid>? ProfileUpdated;

    public ObservableCollection<ProfileDto> Profiles { get; } = [];

    public Guid? HeldRepoId { get; private set; }

    public RemoteChanges? PendingChanges { get; private set; }

    public event EventHandler? PendingChangesChanged;


    public async Task CheckForChanges(CancellationToken cancellationToken)
    {
        if (HeldRepoId is not Guid repoId)
        {
            return;
        }

        var before = Snapshot();
        var profiles = await profileClient.GetProfilesV1Async(repoId, cancellationToken);

        if (HeldRepoId != repoId || before.SequenceEqual(Snapshot()) is false)
        {
            return;
        }

        SetPendingChanges(ProfileListChanges.Between(before, profiles));
    }

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

        HeldRepoId = repoId;
        SetPendingChanges(null);
    }

    /// <summary>
    /// The collection is handed from repo to repo as the user navigates, so on a user change it is
    /// simply handed to nobody.
    /// </summary>
    public void ClearUserState()
    {
        Profiles.Clear();

        HeldRepoId = null;
        SetPendingChanges(null);
    }

    /// <summary>
    /// What the drift check asks so it can say "this folder is on revision 6, the profile is at 8".
    /// </summary>
    /// <remarks>
    /// Answered from <see cref="Profiles"/>, which holds one repo at a time, so this is null for
    /// every profile outside the repo the user is standing in - and null on purpose. Going and
    /// fetching it would put a network round trip per game into a check that runs on every
    /// window activation and is meant to work offline.
    /// </remarks>
    public int? GetHeadRevision(ActiveProfile profile)
    {
        var known = FindProfile(profile.ProfileId);

        return known is not null && known.RepoId == profile.RepoId ? known.HeadRevision : null;
    }

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

        // Ordinarily already absent - it was archived to get here - but a stale page is cheap to
        // tolerate and expensive to assume away.
        if (FindProfile(profileId) is ProfileDto removed)
        {
            Profiles.Remove(removed);
        }
    }

    public async Task ArchiveProfile(Guid repoId, Guid profileId, CancellationToken cancellationToken)
    {
        await profileClient.ArchiveProfileV1Async(repoId, profileId, cancellationToken);

        // The live collection is the sidebar, so an archived profile leaves it the same way a
        // deleted one used to. It has not gone anywhere - the Archive page reads its own list.
        if (FindProfile(profileId) is ProfileDto archived)
        {
            Profiles.Remove(archived);
        }
    }

    public async Task<ProfileDto> RestoreProfile(Guid repoId, Guid profileId, string? name, CancellationToken cancellationToken)
    {
        ProfileDto restored;

        try
        {
            restored = await profileClient.RestoreProfileV1Async(
                repoId, profileId, new RestoreRequest { Name = name }, cancellationToken);
        }
        catch (ApiException<CustomProblemDetails> ex) when (ex.Result.Type == ProblemType.NameTaken)
        {
            throw new UserFriendlyException("Name taken", null, ex);
        }

        Profiles.Add(restored);

        ProfileCreated?.Invoke(restored.Id);

        return restored;
    }

    public async Task<IReadOnlyList<ProfileDto>> GetArchivedProfiles(Guid repoId, CancellationToken cancellationToken)
    {
        return [.. await profileClient.GetArchivedProfilesV1Async(repoId, cancellationToken)];
    }


    public async Task<ProfileModStatistics> GetModStatistics(Guid repoId, Guid profileId, CancellationToken cancellationToken)
    {
        var response = await modDependencyClient.GetModDependenciesV1Async(repoId, profileId, null, cancellationToken);

        return ProfileModStatistics.From(response.Dependencies);
    }

    public async Task<ProfileHistory> GetHistory(Guid repoId, Guid profileId, CancellationToken cancellationToken)
    {
        var response = await profileClient.GetProfileRevisionsV1Async(repoId, profileId, null, null, cancellationToken);

        return new ProfileHistory([.. response.Revisions], response.HeadRevision, response.HasMore);
    }

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

    public void NoteRevisionSaved(Guid profileId, int number)
    {
        if (FindProfile(profileId) is not ProfileDto existing || existing.HeadRevision == number)
        {
            return;
        }

        existing.HeadRevision = number;

        ProfileUpdated?.Invoke(profileId);
    }

    public Task<PruneProfileRevisionsResponse> PruneRevisions(
        Guid repoId, Guid profileId, IReadOnlyList<int> revisions, CancellationToken cancellationToken)
    {
        return profileClient.PruneProfileRevisionsV1Async(
            repoId, profileId, new PruneProfileRevisionsRequest { Revisions = [.. revisions] }, cancellationToken);
    }

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

    /// <summary>
    /// Copies, because the entries are updated in place: a list of the same instances would always
    /// equal itself however much they had changed.
    /// </summary>
    private List<ProfileDto> Snapshot()
    {
        return [.. Profiles.Select(x => x with { })];
    }

    private void SetPendingChanges(RemoteChanges? changes)
    {
        if (PendingChanges is null && changes is null)
        {
            return;
        }

        PendingChanges = changes;
        PendingChangesChanged?.Invoke(this, EventArgs.Empty);
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
