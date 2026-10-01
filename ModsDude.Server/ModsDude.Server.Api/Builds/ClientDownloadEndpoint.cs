using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using ModsDude.Server.Api.ErrorHandling;

namespace ModsDude.Server.Api.Builds;

/// <summary>
/// <c>GET /download</c>: the installer of the client release built with this server, the only client it
/// accepts. Anonymous, outside the API and its document, because it is a link for a browser.
/// </summary>
public static class ClientDownloadEndpoint
{
    private const string InstallerName = "ModsDude.Client-win-Setup.exe";


    public static IEndpointRouteBuilder MapClientDownload(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("download", Get)
            .AllowAnonymous()
            .ExcludeFromDescription();

        return builder;
    }


    private static Results<RedirectHttpResult, NotFound<CustomProblemDetails>> Get(IOptions<ClientDownloadOptions> options)
    {
        // A local build has no release.
        if (ServerBuild.Number == 0)
        {
            return TypedResults.NotFound(Problems.NotFound);
        }

        var repository = options.Value.GithubRepository.TrimEnd('/');

        return TypedResults.Redirect($"{repository}/releases/download/b{ServerBuild.Number}/{InstallerName}");
    }
}
