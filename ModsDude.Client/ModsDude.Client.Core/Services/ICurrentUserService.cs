using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Services;

public interface ICurrentUserService
{
    Task<CurrentUserDto> Get(CancellationToken cancellationToken);
}
