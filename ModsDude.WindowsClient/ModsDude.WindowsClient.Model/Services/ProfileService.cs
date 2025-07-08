using ModsDude.WindowsClient.ApiClient.Generated;
using ModsDude.WindowsClient.Model.Exceptions;

namespace ModsDude.WindowsClient.Model.Services;
public class ProfileService(
    IProfilesClient profileClient)
{
    public delegate void ProfileListChangedEventHandler(Guid? profileIdOfInterest);
    public event ProfileListChangedEventHandler? ProfileListChanged;


    public async Task<IEnumerable<ProfileDto>> GetProfiles(Guid repoId, CancellationToken cancellationToken)
    {
        return await profileClient.GetProfilesV1Async(repoId, cancellationToken);
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
        OnProfileListChanged(profile.Id);
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
        OnProfileListChanged(profileId);
    }

    public async Task DeleteProfile(Guid repoId, Guid profileId, CancellationToken cancellationToken)
    {
        await profileClient.DeleteProfileV1Async(repoId, profileId, cancellationToken);

        OnProfileListChanged(null);
    }

    private void OnProfileListChanged(Guid? idOfInterest)
    {
        ProfileListChanged?.Invoke(idOfInterest);
    }
}
