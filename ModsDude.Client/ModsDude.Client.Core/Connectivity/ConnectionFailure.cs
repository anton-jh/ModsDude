using ModsDude.Client.Core.Accounts;
using ModsDude.Client.Core.Builds;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using System.Net;
using System.Net.Sockets;

namespace ModsDude.Client.Core.Connectivity;

/// <summary>
/// Whether an exception means "nobody answered" rather than "somebody answered no".
/// </summary>
/// <remarks>
/// <para>
/// <b>The difference is whether waiting helps.</b> A refused connection, a timeout or a gateway with
/// nothing behind it - the server restarting, a network that is not up yet - fixes itself, so it is
/// worth trying again without asking anybody. A 400 or a 500 is the server's actual answer and will
/// be the same answer next time, so it stays an error somebody is shown.
/// </para>
/// <para>
/// <b>A server that refuses this build counts too</b>: it ends by the update arriving or the server
/// catching up, and <see cref="IServerCompatibility"/> is what says so on screen. So does a blocked
/// account: it ends by being unblocked, and <see cref="IAccountStatus"/> says so on screen.
/// </para>
/// <para>
/// <b>Down the whole inner chain</b>, because the thing that failed is rarely the thing that throws:
/// the sign-in library wraps the socket error it got, and every server call acquires a token first.
/// </para>
/// </remarks>
public static class ConnectionFailure
{
    /// <param name="alsoCounts">
    /// Anything else that means the same thing, for exception types Core cannot see - the sign-in
    /// library's, which the WPF client owns. Asked of every exception in the chain.
    /// </param>
    public static bool Is(Exception exception, Func<Exception, bool>? alsoCounts = null)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var counts = current switch
            {
                _ when BuildRefusal.Is(current) => true,
                _ when AccountRefusal.Is(current) => true,
                ApiException api => IsGateway(api.StatusCode),
                HttpRequestException { StatusCode: null } => true,
                HttpRequestException { StatusCode: HttpStatusCode status } => IsGateway((int)status),
                SocketException => true,
                // What HttpClient puts inside the cancellation it throws when its own timeout fires.
                TimeoutException => true,
                _ => alsoCounts?.Invoke(current) ?? false
            };

            if (counts)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A proxy in front of a server that is not there yet, or not up yet.</summary>
    private static bool IsGateway(int status) => status is 502 or 503 or 504;
}
