using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Core.Services;
public class ProfileService(
    IProfilesClient profileClient,
    IModDependenciesClient modDependencyClient)
{
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
