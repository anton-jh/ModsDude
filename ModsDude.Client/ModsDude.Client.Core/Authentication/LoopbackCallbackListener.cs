using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ModsDude.Client.Core.Authentication;

/// <summary>
/// Listens on a free loopback port for the browser to come back from the sign-in page (RFC 8252). The
/// server accepts any port on <c>http://localhost/callback/</c> for this client.
/// </summary>
public sealed class LoopbackCallbackListener : IDisposable
{
    private const int _attempts = 5;

    private readonly HttpListener _listener;


    private LoopbackCallbackListener(HttpListener listener, Uri redirectUri)
    {
        _listener = listener;
        RedirectUri = redirectUri;
    }


    public Uri RedirectUri { get; }


    /// <summary>
    /// A port is picked by the system and then claimed, which another program can win in between, so
    /// a lost port is simply tried again with another.
    /// </summary>
    public static LoopbackCallbackListener Start()
    {
        for (var attempt = 1; ; attempt++)
        {
            var redirectUri = new Uri($"http://localhost:{FreePort()}/callback/");
            var listener = new HttpListener();
            listener.Prefixes.Add(redirectUri.ToString());

            try
            {
                listener.Start();

                return new LoopbackCallbackListener(listener, redirectUri);
            }
            catch (HttpListenerException) when (attempt < _attempts)
            {
                listener.Close();
            }
        }
    }

    /// <summary>
    /// The query of the first request that carries <paramref name="expectedState"/>. Any other request
    /// is answered with an error and ignored: it did not come from the sign-in this listener started.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> WaitForCallbackAsync(string expectedState, CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(_listener.Stop);

        while (true)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception exception) when (cancellationToken.IsCancellationRequested && exception is HttpListenerException or ObjectDisposedException)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            var query = context.Request.QueryString;
            var parameters = query.AllKeys
                .Where(x => x is not null)
                .ToDictionary(x => x!, x => query[x] ?? "", StringComparer.Ordinal);

            if (!parameters.TryGetValue("state", out var state) || state != expectedState)
            {
                await RespondAsync(context, HttpStatusCode.BadRequest, "This is not the sign-in ModsDude is waiting for.");
                continue;
            }

            var succeeded = !parameters.ContainsKey("error");
            await RespondAsync(
                context,
                HttpStatusCode.OK,
                succeeded ? "You're signed in. You can close this tab and return to ModsDude." : "Sign-in did not finish. Return to ModsDude to try again.");

            return parameters;
        }
    }

    public void Dispose()
    {
        _listener.Close();
    }


    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();

        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    private static async Task RespondAsync(HttpListenerContext context, HttpStatusCode status, string message)
    {
        var body = Encoding.UTF8.GetBytes($$"""
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="utf-8">
            <meta name="color-scheme" content="dark">
            <title>ModsDude</title>
            <style>
                html, body { margin: 0; min-height: 100%; background: #121212; color: #e8e8e8; font-family: system-ui, sans-serif; }
                body { display: grid; place-items: center; min-height: 100vh; text-align: center; padding: 0 16px; }
            </style>
            </head>
            <body><p>{{WebUtility.HtmlEncode(message)}}</p></body>
            </html>
            """);

        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = body.Length;

        await context.Response.OutputStream.WriteAsync(body);
        context.Response.Close();
    }
}
