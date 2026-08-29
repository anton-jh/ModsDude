using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Mods;

namespace ModsDude.Server.Api.Endpoints.Images;

/// <summary>
/// Mod files go straight to blob storage over a SAS because they are large and fetched rarely.
/// Images invert both properties, so they invert the answer: drawing one list would mint tens of
/// thousands of SAS URLs. They are served through the API instead, which is affordable precisely
/// because a content-addressed image is immutable and therefore cacheable forever — each one crosses
/// the wire once per machine, ever.
///
/// Authorization is <b>authenticated user</b>, for the reason set out on
/// <see cref="CheckImagesExistV1Endpoint"/>: the route carries no repo and cannot.
/// </summary>
public class GetImageV1Endpoint : IEndpoint
{
    private static readonly TimeSpan _cacheLifetime = TimeSpan.FromDays(365);


    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        // Declared explicitly: a streamed body carries no response type the document generator can
        // infer, and without it the generated client has no success case for the one route whose
        // whole purpose is returning bytes.
        return builder.MapGet("images/{hash}", Get)
            .WithTags("Images")
            .Produces<FileResult>(StatusCodes.Status200OK, "application/octet-stream");
    }


    private static async Task<Results<FileStreamHttpResult, BadRequest<CustomProblemDetails>>> Get(
        string hash,
        HttpContext httpContext,
        IModImageStorageService imageStorageService,
        CancellationToken cancellationToken)
    {
        if (!ModImageHash.IsValid(hash))
        {
            return TypedResults.BadRequest(Problems.InvalidImageHash(hash));
        }

        var image = await imageStorageService.Download(hash, cancellationToken);
        if (image is null)
        {
            return TypedResults.BadRequest(Problems.ImageDoesNotExist(hash));
        }

        // The address is the hash of the bytes, so the response can never go stale and the entity
        // tag is the address itself.
        httpContext.Response.Headers.CacheControl = $"public, max-age={(int)_cacheLifetime.TotalSeconds}, immutable";

        return TypedResults.File(image.Content, image.ContentType, entityTag: new EntityTagHeaderValue($"\"{hash}\""));
    }
}
