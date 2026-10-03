using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Services;

public interface ITrustCodeService
{
    /// <summary>Makes the signed-in user trusted, and returns them as they now are.</summary>
    Task<CurrentUserDto> Redeem(string code, CancellationToken cancellationToken);
}
