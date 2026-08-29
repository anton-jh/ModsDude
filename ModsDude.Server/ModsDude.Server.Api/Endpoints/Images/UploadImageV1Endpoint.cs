using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Mods;
using System.Security.Cryptography;

namespace ModsDude.Server.Api.Endpoints.Images;

/// <summary>
/// Takes one image derivative, generated client-side at import — only the client can decode DDS,
/// including the managed BC7 path, and the server has no business opening mod files.
///
/// The bytes are hashed here and refused unless they hash to the address they were sent to. A
/// permanently cached, globally shared address space is only safe when every ingest is verified;
/// doing it on the way in stops a bad address existing at all, rather than leaving every reader to
/// detect it separately, and it costs a hash of a few kilobytes.
///
/// Authorization is <b>authenticated user</b>, for the reason set out on
/// <see cref="CheckImagesExistV1Endpoint"/>: the route carries no repo and cannot.
/// </summary>
public class UploadImageV1Endpoint : IEndpoint
{
    /// <summary>
    /// Sources top out at 1024 px and a full derivative measures ~50 KB, so this is a safety net
    /// rather than a working limit.
    /// </summary>
    private const long _maximumImageSize = 8 * 1024 * 1024;

    private const string _defaultContentType = "application/octet-stream";


    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("images/{hash}", Upload)
            .WithTags("Images")
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(_maximumImageSize));
    }


    private static async Task<Results<Ok, BadRequest<CustomProblemDetails>>> Upload(
        string hash,
        IFormFile image,
        IModImageStorageService imageStorageService,
        CancellationToken cancellationToken)
    {
        if (!ModImageHash.IsValid(hash))
        {
            return TypedResults.BadRequest(Problems.InvalidImageHash(hash));
        }

        string actualHash;
        using (var forHashing = image.OpenReadStream())
        {
            actualHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(forHashing, cancellationToken));
        }

        if (actualHash != hash)
        {
            return TypedResults.BadRequest(Problems.ImageHashMismatch(hash, actualHash));
        }

        using var forUpload = image.OpenReadStream();
        var contentType = string.IsNullOrWhiteSpace(image.ContentType) ? _defaultContentType : image.ContentType;

        await imageStorageService.Upload(hash, contentType, forUpload, cancellationToken);

        return TypedResults.Ok();
    }
}
