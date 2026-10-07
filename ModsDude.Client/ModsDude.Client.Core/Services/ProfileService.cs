using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;

namespace ModsDude.Client.Core.Services;
public class ProfileService(
    IProfilesClient profileClient,
    IModDependenciesClient modDependencyClient,
    IModsClient modsClient)
    : IProfileService
{
    /// <summary>Only ever walked to the end, so the page size is a round-trip count, not a UI concern.</summary>
    private const int _modPageSize = 200;


    public async Task<ProfileDto?> FindProfile(Guid repoId, Guid profileId, CancellationToken cancellationToken)
    {
        try
        {
            return await profileClient.GetProfileV1Async(repoId, profileId, cancellationToken);
        }
        catch (ApiException<CustomProblemDetails> ex) when (ex.Result.Type == ProblemType.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<ProfileDto>> GetArchivedProfiles(Guid repoId, CancellationToken cancellationToken)
    {
        return [.. await profileClient.GetArchivedProfilesV1Async(repoId, cancellationToken)];
    }

    public async Task<ProfileModStatistics> GetModStatistics(Guid repoId, Guid profileId, CancellationToken cancellationToken)
    {
        var response = await modDependencyClient.GetModDependenciesV1Async(repoId, profileId, null, cancellationToken);

        return ProfileModStatistics.From(response);
    }

    public async Task<ProfileHistory> GetHistory(Guid repoId, Guid profileId, CancellationToken cancellationToken)
    {
        var response = await profileClient.GetProfileRevisionsV1Async(repoId, profileId, null, null, cancellationToken);

        return new ProfileHistory([.. response.Revisions], response.HeadRevision, response.HasMore);
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
}


/// <param name="HasMore">
/// Whether older revisions were left unread. The listing is windowed from the newest, and nothing
/// yet asks for a second page.
/// </param>
public sealed record ProfileHistory(IReadOnlyList<ProfileRevisionDto> Revisions, int HeadRevision, bool HasMore);
