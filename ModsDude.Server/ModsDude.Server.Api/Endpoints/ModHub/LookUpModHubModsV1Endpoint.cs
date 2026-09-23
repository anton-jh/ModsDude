using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.ModHub;
using ModsDude.Server.ModHub;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Endpoints.ModHub;

/// <summary>
/// "Which of these files does ModHub have, and at what version?", answered from what the crawler has
/// stored - nothing here reaches ModHub.
/// </summary>
/// <remarks>
/// <para>
/// <b>Names, not a profile.</b> A client asks about whatever it is looking at: an unsaved draft, a
/// game's mod folder, Downloads. Any of them can hold files the repo has never registered, and the
/// server has no reason to learn how a repo's mod ids relate to a ModHub file name.
/// </para>
/// <para>
/// Authorization is <b>authenticated user</b>. What is answered is public on ModHub, and nothing in
/// it says anything about a repo.
/// </para>
/// </remarks>
public class LookUpModHubModsV1Endpoint : IEndpoint
{
    /// <summary>A few times the largest mod folder anybody keeps, and one indexed query whatever the size.</summary>
    private const int _maximumBatchSize = 5000;


    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("modhub/{game}/lookup", LookUp)
            .WithTags("ModHub");
    }


    /// <param name="Names">
    /// Mod file names, with or without the <c>.zip</c>, matched ignoring case.
    /// </param>
    public record LookUpModHubModsRequest(IEnumerable<string> Names);

    /// <param name="CurrentAsOf">
    /// Changes ModHub made up to this moment are in the answer. Null while the server has not finished
    /// reading ModHub for the first time, in which case the answer is missing most mods and a client
    /// should say so rather than present it as "nothing newer".
    /// </param>
    public record LookUpModHubModsResponse(DateTimeOffset? CurrentAsOf, IEnumerable<ModHubModDto> Mods);

    /// <param name="Name">The requested name this answers, exactly as it was sent.</param>
    /// <param name="FileName">The file name as ModHub gives it.</param>
    /// <param name="Version">ModHub's version string, unparsed.</param>
    /// <param name="PageUrl">The mod's page on ModHub, for a person to open.</param>
    /// <param name="DownloadUrl">The archive on ModHub's CDN, where the page links one.</param>
    public record ModHubModDto(
        string Name,
        int ModHubId,
        string FileName,
        string Title,
        string Version,
        string PageUrl,
        string? DownloadUrl);


    private static async Task<Results<Ok<LookUpModHubModsResponse>, BadRequest<CustomProblemDetails>>> LookUp(
        string game,
        LookUpModHubModsRequest request,
        ApplicationDbContext dbContext,
        IModHubSite site,
        IOptions<ModHubOptions> options,
        CancellationToken cancellationToken)
    {
        if (options.Value.Games.Contains(game) is false)
        {
            return TypedResults.BadRequest(Problems.UnknownModHubGame(game));
        }

        var names = request.Names.Where(x => string.IsNullOrWhiteSpace(x) is false).Distinct().ToList();

        if (names.Count > _maximumBatchSize)
        {
            return TypedResults.BadRequest(Problems.BatchTooLarge(names.Count, _maximumBatchSize));
        }

        var state = await dbContext.ModHubCrawlStates.FindAsync([game], cancellationToken);

        var keys = names.Select(ModHubMod.ToFileNameKey).ToHashSet();
        var byKey = (await dbContext.ModHubMods.GetByFileNameKeysAsync(game, keys, cancellationToken))
            .ToLookup(x => x.FileNameKey);

        var mods = names
            .SelectMany(name => byKey[ModHubMod.ToFileNameKey(name)].Select(mod => new ModHubModDto(
                name,
                mod.ModHubId,
                mod.FileName,
                mod.Title,
                mod.Version,
                site.GetModPageUrl(game, mod.ModHubId),
                mod.DownloadUrl)))
            .ToList();

        return TypedResults.Ok(new LookUpModHubModsResponse(state?.CurrentAsOf, mods));
    }
}
