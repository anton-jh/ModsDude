using ModsDude.Server.Api.ErrorHandling;
using System.Globalization;

namespace ModsDude.Server.Api.Builds;

/// <summary>
/// Refuses any API request from a client whose build is not this server's. The API changes without
/// compatibility shims, so only an exact match is known to work.
/// </summary>
/// <remarks>
/// Development lets a request without the header through, so Swagger UI can call the API. A header that
/// is present still has to match.
/// </remarks>
public sealed class ClientBuildMiddleware(IHostEnvironment environment) : IMiddleware
{
    public const string HeaderName = "X-ModsDude-Build";


    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var header = context.Request.Headers[HeaderName];

        if (header.Count == 0 && environment.IsDevelopment())
        {
            await next(context);

            return;
        }

        int? clientBuild = int.TryParse(header, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

        if (clientBuild == ServerBuild.Number)
        {
            await next(context);

            return;
        }

        context.Response.StatusCode = StatusCodes.Status412PreconditionFailed;

        await context.Response.WriteAsJsonAsync(
            Problems.ClientBuildMismatch(clientBuild, ServerBuild.Number),
            context.RequestAborted);
    }
}
