using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Mods;

namespace ModsDude.Server.Api.Endpoints.Images;

/// <summary>
/// "Which of these do you already have?", asked before an import uploads anything. After the first
/// import into a repo almost every image is already stored — versions of one mod reuse their
/// artwork, and so do mods across repos — so 2,000 mods at ~20 images each is tens of thousands of
/// uploads that mostly need not happen.
///
/// Authorization is <b>authenticated user</b>, not Guest of a repo. Content addressing is what makes
/// that dedupe possible and it leaves no repo in the address, so there is nothing to scope against;
/// this is an existence oracle over every image in the system. It is a real widening compared to the
/// rest of the server, stated here rather than hidden behind a Guest label that would imply a
/// scoping this route does not have. What is behind an address is mod store art, already public on
/// the sites the mods come from, and it reveals nothing about who is in which repo.
/// </summary>
public class CheckImagesExistV1Endpoint : IEndpoint
{
    /// <summary>
    /// Each hash costs a round trip to blob storage, which has no batch existence call, so an
    /// unbounded batch is an unbounded amount of work for one request.
    /// </summary>
    private const int _maximumBatchSize = 1000;


    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("images/checkExisting", CheckExisting)
            .WithTags("Images");
    }


    public record CheckImagesExistRequest(IEnumerable<string> Hashes);
    public record CheckImagesExistResponse(IEnumerable<string> Present);


    private static async Task<Results<Ok<CheckImagesExistResponse>, BadRequest<CustomProblemDetails>>> CheckExisting(
        CheckImagesExistRequest request,
        IModImageStorageService imageStorageService,
        CancellationToken cancellationToken)
    {
        var hashes = request.Hashes.ToList();

        if (hashes.Count > _maximumBatchSize)
        {
            return TypedResults.BadRequest(Problems.BatchTooLarge(hashes.Count, _maximumBatchSize));
        }

        if (hashes.FirstOrDefault(x => !ModImageHash.IsValid(x)) is string invalid)
        {
            return TypedResults.BadRequest(Problems.InvalidImageHash(invalid));
        }

        var present = await imageStorageService.CheckWhichExist(hashes, cancellationToken);

        return TypedResults.Ok(new CheckImagesExistResponse(present));
    }
}
