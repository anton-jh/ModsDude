using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Exceptions;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;
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
/// The route carries no repo and cannot, for the reason set out on <see cref="CheckImagesExistV1Endpoint"/>,
/// so it is open to a user who is trusted or belongs to at least one repo: anybody with a reason to
/// have a picture or a mod to show, and nobody who has only signed in. Only WebP is taken, and it is
/// served as WebP whatever the uploader called it.
/// </summary>
public class UploadImageV1Endpoint : IEndpoint
{
    /// <summary>
    /// Sources top out at 1024 px and a full derivative measures ~50 KB, so this is a safety net
    /// rather than a working limit.
    /// </summary>
    private const long _maximumImageSize = 8 * 1024 * 1024;



    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("images/{hash}", Upload)
            .WithTags("Images")
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(_maximumImageSize));
    }


    private static async Task<Results<Ok, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> Upload(
        string hash,
        IFormFile image,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        IModImageStorageService imageStorageService,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            ?? throw new NotAuthenticatedException();

        if (!user.IsTrusted && !user.RepoMemberships.Any())
        {
            return new Forbidden<CustomProblemDetails>(Problems.NotTrusted);
        }

        if (!Sha256Hex.IsValid(hash))
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

        using (var forSniffing = image.OpenReadStream())
        {
            if (!await IsWebPAsync(forSniffing, cancellationToken))
            {
                return TypedResults.BadRequest(Problems.InvalidImageFormat);
            }
        }

        using var forUpload = image.OpenReadStream();

        await imageStorageService.Upload(hash, forUpload, cancellationToken);

        return TypedResults.Ok();
    }

    /// <summary>A RIFF container whose form type is WEBP, which is how every WebP file starts.</summary>
    private static async Task<bool> IsWebPAsync(Stream content, CancellationToken cancellationToken)
    {
        var header = new byte[12];
        var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);

        return read == header.Length
            && header.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            && header.AsSpan(8, 4).SequenceEqual("WEBP"u8);
    }
}
