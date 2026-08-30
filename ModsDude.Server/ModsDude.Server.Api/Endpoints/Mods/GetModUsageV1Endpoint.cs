using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Buffers.Text;
using System.Globalization;
using System.Security.Claims;
using System.Text;

namespace ModsDude.Server.Api.Endpoints.Mods;

/// <summary>
/// Which registered versions the repo's profiles actually depend on, so the Manage page can offer an
/// "Unused" filter and tell someone whether a delete will be refused before they try it.
/// </summary>
/// <remarks>
/// <para>
/// A resource of its own rather than a field on <see cref="ModDto"/>. The mod list is paginated and
/// has a delta form keyed on <c>ModVersion.Updated</c>, and usage changes when a *profile* is
/// edited, not when a version is. Putting it on the version would leave every incremental sync
/// showing usage from whenever the row last moved — or force a restamp of every version a profile
/// save touches, which for a profile of two thousand mods writes two thousand rows and turns the
/// delta into a full listing. Two facts with different lifetimes, so two resources.
/// </para>
/// <para>
/// It is deliberately not authoritative. The delete endpoints refuse a version a profile depends on
/// and the foreign key refuses it again underneath them; this exists to show the user the truth
/// before they try, not to enforce anything.
/// </para>
/// </remarks>
public class GetModUsageV1Endpoint : IEndpoint
{
    /// <summary>
    /// Larger than the mod listing's, because a usage row is two identifiers and a count rather than
    /// a version's whole metadata, and this is read as a snapshot rather than page by page as the
    /// user scrolls.
    /// </summary>
    private const int _defaultLimit = 1000;

    private const int _maximumLimit = 5000;


    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        // A literal segment beats the parameter in `mods/{modId}`, and no mod route answers GET
        // anyway, so there is nothing for a mod called "usage" to shadow.
        return builder.MapGet("repos/{repoId:guid}/mods/usage", Get)
            .WithTags("Mods");
    }


    /// <param name="cursor">Opaque, from a previous response's <c>NextCursor</c>.</param>
    public async Task<Results<Ok<GetModUsageResponse>, BadRequest<CustomProblemDetails>>> Get(
        Guid repoId,
        string? cursor,
        int? limit,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(repoId), RepoMembershipLevel.Guest))
            .MapToBadRequest();
        if (authResult is not null)
        {
            return authResult;
        }

        UsageCursor? resumePoint = null;
        if (cursor is not null && !UsageCursor.TryDecode(cursor, out resumePoint))
        {
            return TypedResults.BadRequest(Problems.InvalidCursor(cursor));
        }

        var pageSize = Math.Clamp(limit ?? _defaultLimit, 1, _maximumLimit);
        var taken = resumePoint?.Taken ?? 0;

        var page = await dbContext.Profiles.GetModUsageAsync(new RepoId(repoId), taken, pageSize, cancellationToken);

        return TypedResults.Ok(new GetModUsageResponse(
            page.Select(ModUsageDto.FromModel),
            page.Count < pageSize ? null : new UsageCursor(taken + page.Count).Encode()));
    }


    /// <param name="Usage">
    /// One entry per version at least one profile pins, ordered by mod and then version. A version
    /// that does not appear is unused — but only once the whole listing has been read, so a client
    /// must exhaust <paramref name="NextCursor"/> before treating an absence as an answer. Acting on
    /// a partial view is the hazard this endpoint exists to remove.
    /// </param>
    /// <param name="NextCursor">
    /// <c>null</c> once the listing is exhausted. Unlike the mod listing there is no delta form: a
    /// dependency carries no timestamp of its own, and the whole set is small enough to refetch.
    /// </param>
    public record GetModUsageResponse(IEnumerable<ModUsageDto> Usage, string? NextCursor);


    /// <summary>
    /// How far into the listing the client has read. An offset rather than the last key returned,
    /// because the ids are value objects and a provider cannot translate a comparison on one — the
    /// same constraint the mod listing's cursor works around.
    /// </summary>
    /// <remarks>
    /// An offset shifts under concurrent profile edits, so a page can repeat or miss a row while
    /// somebody else is saving a profile. That is acceptable here and only here: the answer is
    /// advisory, and the delete endpoints re-ask the database the moment it matters.
    /// </remarks>
    private record UsageCursor(int Taken)
    {
        public string Encode()
        {
            return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(Taken.ToString(CultureInfo.InvariantCulture)));
        }

        public static bool TryDecode(string cursor, out UsageCursor? decoded)
        {
            decoded = null;

            if (!Base64Url.IsValid(cursor))
            {
                return false;
            }

            if (!int.TryParse(Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor)), CultureInfo.InvariantCulture, out var taken)
                || taken < 0)
            {
                return false;
            }

            decoded = new UsageCursor(taken);

            return true;
        }
    }
}
