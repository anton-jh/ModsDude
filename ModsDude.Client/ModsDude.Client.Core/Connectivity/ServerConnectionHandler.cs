using System.Net;

namespace ModsDude.Client.Core.Connectivity;

/// <summary>
/// Sorts every server request into "answered" and "nobody answered", which is all
/// <see cref="IServerConnection"/> knows.
/// </summary>
/// <remarks>
/// <para>
/// <b>Any answer counts as reachable</b>, errors included: a 400 or a 500 is the server speaking.
/// Except a gateway status, which is the proxy in front of it saying the server is not there - the
/// same line <see cref="ConnectionFailure"/> draws.
/// </para>
/// <para>
/// <b>A cancellation says nothing either way.</b> Inside the handler a caller giving up and the
/// client's own timeout look the same, and the caller giving up is not the server going away.
/// </para>
/// </remarks>
public sealed class ServerConnectionHandler(IServerConnection connection) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            connection.ReportUnreachable();

            throw;
        }

        if (response.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
        {
            connection.ReportUnreachable();
        }
        else
        {
            connection.ReportReachable();
        }

        return response;
    }
}
