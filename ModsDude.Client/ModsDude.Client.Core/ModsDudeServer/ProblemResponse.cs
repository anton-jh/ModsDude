using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using Newtonsoft.Json;

namespace ModsDude.Client.Core.ModsDudeServer;

public static class ProblemResponse
{
    /// <summary>
    /// The problem a failed response carries, or null where its body is not one. Buffered, so the
    /// generated client can read the body again after this.
    /// </summary>
    public static async Task<CustomProblemDetails?> ReadAsync(HttpResponseMessage response, ILogger logger, CancellationToken cancellationToken)
    {
        await response.Content.LoadIntoBufferAsync(cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            return JsonConvert.DeserializeObject<CustomProblemDetails>(body);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Could not read a {Status} response as a problem.", (int)response.StatusCode);

            return null;
        }
    }
}
