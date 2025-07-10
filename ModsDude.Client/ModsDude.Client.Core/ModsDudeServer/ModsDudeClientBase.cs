using System.Net.Http.Headers;

namespace ModsDude.Client.Core.ModsDudeServer;
public abstract class ModsDudeClientBase(
    ClientConfiguration clientConfiguration)
{
    protected async Task<HttpRequestMessage> CreateHttpRequestMessageAsync(CancellationToken cancellationToken)
    {
        var msg = new HttpRequestMessage();

        msg.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await clientConfiguration.AccessTokenAccessor.Get(cancellationToken));

        return msg;
    }
}
