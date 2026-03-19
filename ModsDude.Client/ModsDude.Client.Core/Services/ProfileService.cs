using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Exceptions;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Core.Services;
public class ProfileService(
    IProfilesClient profileClient)
{
    public delegate void ProfileOfInterestChangedEventHandler(Guid profileIdOfInterest);
    public event ProfileOfInterestChangedEventHandler? ProfileOfInterestChanged;

    public ObservableCollection<ProfileDto> Profiles { get; } = [];


    public async Task RefreshProfiles(Guid repoId, CancellationToken cancellationToken)
    {
        var profiles = await profileClient.GetProfilesV1Async(repoId, cancellationToken);
        
        Profiles.Clear();

        foreach (var profile in profiles)
        {
            Profiles.Add(profile);
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
        catch (ApiException ex) when (ex.StatusCode == 409)
        {
            throw new UserFriendlyException("Name taken", null, ex);
        }

        await RefreshProfiles(repoId, cancellationToken);

        OnProfileOfInterestChanged(profile.Id);
    }

    public async Task UpdateProfile(Guid repoId, Guid profileId, string name, CancellationToken cancellationToken)
    {
        var request = new UpdateProfileRequest()
        {
            Name = name
        };

        try
        {
            await profileClient.UpdateProfileV1Async(repoId, profileId, request, cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 409)
        {
            throw new UserFriendlyException("Name taken", null, ex);
        }

        await RefreshProfiles(repoId, cancellationToken);

        OnProfileOfInterestChanged(profileId);
    }

    public async Task DeleteProfile(Guid repoId, Guid profileId, CancellationToken cancellationToken)
    {
        await profileClient.DeleteProfileV1Async(repoId, profileId, cancellationToken);

        await RefreshProfiles(repoId, cancellationToken);
    }

    private void OnProfileOfInterestChanged(Guid idOfInterest)
    {
        ProfileOfInterestChanged?.Invoke(idOfInterest);
    }
}
