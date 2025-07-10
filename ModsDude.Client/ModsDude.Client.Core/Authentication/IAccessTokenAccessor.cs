namespace ModsDude.Client.Core.Authentication;

public interface IAccessTokenAccessor
{
    Task<string> Get(CancellationToken cancellationToken);
}
