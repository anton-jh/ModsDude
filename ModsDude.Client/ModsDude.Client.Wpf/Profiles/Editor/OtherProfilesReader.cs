using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;

namespace ModsDude.Client.Wpf.Profiles.Editor;

/// <summary>The repo's other profiles, and what each pins now.</summary>
public sealed class OtherProfilesReader(
    IProfilesClient profilesClient,
    IModDependenciesClient dependenciesClient,
    Guid repoId,
    Guid profileId)
{
    public async Task<List<ProfileDto>> ListAsync(CancellationToken cancellationToken)
    {
        var profiles = await profilesClient.GetProfilesV1Async(repoId, cancellationToken);

        return [.. profiles.Where(x => x.Id != profileId).OrderBy(x => x.Name, NaturalOrder.Comparer)];
    }

    public async Task<IReadOnlyList<ProfileModPin>> ReadPinsAsync(Guid otherProfileId, CancellationToken cancellationToken)
        => ToPins(await dependenciesClient.GetModDependenciesV1Async(repoId, otherProfileId, null, cancellationToken));

    public static IReadOnlyList<ProfileModPin> ToPins(GetModDependenciesResponse response)
        => [.. response.Dependencies.Select(x => new ProfileModPin(
            ModKey.From(x.ModId),
            ModVersionKey.From(x.ModVersionId),
            new ProfileModLock(false, x.Locked)))];
}
