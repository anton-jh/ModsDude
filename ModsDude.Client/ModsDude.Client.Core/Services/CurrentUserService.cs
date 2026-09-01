using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Services;

/// <summary>
/// The signed-in user as <i>this system</i> knows them.
/// </summary>
/// <remarks>
/// The display name is the token's own and needs no round trip. Two things are not: the tag, derived
/// from the subject id by a rule that lives on the server, and <c>IsTrusted</c>, which decides
/// whether this user may create repos at all. Neither can be worked out here, and the second is why
/// the shell can close that option with an explanation instead of letting a form be filled in and
/// refused.
/// </remarks>
public class CurrentUserService(IUsersClient usersClient)
{
    public Task<CurrentUserDto> Get(CancellationToken cancellationToken)
    {
        return usersClient.GetCurrentUserV1Async(cancellationToken);
    }
}
