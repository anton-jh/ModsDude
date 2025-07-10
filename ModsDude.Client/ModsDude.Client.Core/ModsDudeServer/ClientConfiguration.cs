using ModsDude.Client.Core.Authentication;

namespace ModsDude.Client.Core.ModsDudeServer;
public class ClientConfiguration(IAccessTokenAccessor accessTokenAccessor)
{
    public IAccessTokenAccessor AccessTokenAccessor { get; } = accessTokenAccessor;
}
