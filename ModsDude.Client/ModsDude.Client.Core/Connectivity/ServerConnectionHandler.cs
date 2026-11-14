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
/// <b>The timeout is this handler's, not the client's</b>, because only here can a server that never
/// answers be told apart from a caller giving up - which says nothing either way. The client's own
/// timeout is switched off where this handler is used.
/// </para>
/// </remarks>
public sealed class ServerConnectionHandler(IServerConnection connection, TimeProvider time) : DelegatingHandler
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(100);


    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(RequestTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        HttpResponseMessage response;

        try
        {
            response = await base.SendAsync(request, linked.Token);
        }
        catch (HttpRequestException)
        {
            connection.ReportUnreachable();

            throw;
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested && cancellationToken.IsCancellationRequested is false)
        {
            connection.ReportUnreachable();

            // The shape HttpClient gives its own timeout, which ConnectionFailure recognises.
            throw new TaskCanceledException(
                $"The server did not answer within {RequestTimeout.TotalSeconds:0} seconds.",
                new TimeoutException(exception.Message, exception));
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
