using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Api.Endpoints.Images;

/// <summary>
/// "Which of these do you already have?", asked before an import uploads anything. After the first
/// import into a repo almost every image is already stored — versions of one mod reuse their
/// artwork, and so do mods across repos — so 2,000 mods at ~20 images each is tens of thousands of
/// uploads that mostly need not happen.
///
/// Answered from the database: an image is "had" when something refers to it. One query instead of a
/// call to storage per hash, and an image that was uploaded but never referenced is uploaded again,
/// which is cheap and changes nothing. Whatever then refers to it checks storage itself.
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
    /// <summary>Keeps the query's parameter list bounded.</summary>
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
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var hashes = request.Hashes.ToList();

        if (hashes.Count > _maximumBatchSize)
        {
            return TypedResults.BadRequest(Problems.BatchTooLarge(hashes.Count, _maximumBatchSize));
        }

        if (hashes.FirstOrDefault(x => !Sha256Hex.IsValid(x)) is string invalid)
        {
            return TypedResults.BadRequest(Problems.InvalidImageHash(invalid));
        }

        var distinct = hashes.Distinct().ToList();

        var onVersions = await dbContext.ModVersions
            .AsNoTracking()
            .SelectMany(x => x.Images)
            .Where(x => distinct.Contains(x.Hash))
            .Select(x => x.Hash)
            .Distinct()
            .ToListAsync(cancellationToken);

        var asAvatars = await dbContext.Users
            .AsNoTracking()
            .Where(x => x.AvatarHash != null && distinct.Contains(x.AvatarHash))
            .Select(x => x.AvatarHash!)
            .Distinct()
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new CheckImagesExistResponse([.. onVersions.Union(asAvatars).Order(StringComparer.Ordinal)]));
    }
}
