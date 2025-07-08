using ModsDude.WindowsClient.Model.Services;
using System.Net.Http.Headers;

namespace ModsDude.WindowsClient.Model.ModsDudeServer;
public abstract class ModsDudeClientBase(
    SessionService sessionService)
{
    protected async Task<HttpRequestMessage> CreateHttpRequestMessageAsync(CancellationToken cancellationToken)
    {
        var msg = new HttpRequestMessage();

        msg.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            (await sessionService.GetSession(cancellationToken)).AccessToken);

        return msg;
    }
}
