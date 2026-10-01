using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using Newtonsoft.Json;
using System.Globalization;
using System.Net;

namespace ModsDude.Client.Core.Builds;

/// <summary>
/// Tells the server which build every request comes from, and reports to
/// <see cref="IServerCompatibility"/> what it answered. The response is passed on untouched, so the
/// caller still sees the refusal as the API error it is.
/// </summary>
public sealed class BuildHeaderHandler(
    BuildNumber client,
    IServerCompatibility compatibility,
    ILogger<BuildHeaderHandler> logger) : DelegatingHandler
{
    public const string HeaderName = "X-ModsDude-Build";


    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Add(HeaderName, client.Value.ToString(CultureInfo.InvariantCulture));

        var response = await base.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            compatibility.ReportAccepted();
        }
        else if (response.StatusCode == HttpStatusCode.PreconditionFailed
            && await ReadServerBuildAsync(response, cancellationToken) is int server)
        {
            compatibility.ReportRefused(new BuildNumber(server));
        }

        return response;
    }


    private async Task<int?> ReadServerBuildAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // Buffered, so the generated client can read the body again after this.
        await response.Content.LoadIntoBufferAsync(cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            var problem = JsonConvert.DeserializeObject<CustomProblemDetails>(body);

            if (problem is { Type: ProblemType.ClientBuildMismatch, ServerBuild: int server })
            {
                return server;
            }
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Could not read a {Status} response as a problem.", (int)response.StatusCode);

            return null;
        }

        logger.LogWarning("A {Status} response was not a build mismatch: {Body}", (int)response.StatusCode, body);

        return null;
    }
}
