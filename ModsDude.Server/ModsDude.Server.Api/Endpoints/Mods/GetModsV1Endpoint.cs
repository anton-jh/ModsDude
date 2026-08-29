using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Buffers.Text;
using System.Globalization;
using System.Security.Claims;
using System.Text;

namespace ModsDude.Server.Api.Endpoints.Mods;

public class GetModsV1Endpoint : IEndpoint
{
    private const int _defaultLimit = 100;
    private const int _maximumLimit = 500;


    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("repos/{repoId:guid}/mods", GetAll)
            .WithTags("Mods");
    }


    /// <param name="updatedAfter">
    /// Exclusive. Bounds the steady state, which pagination cannot: a repeated sync asks only for
    /// what changed. Ignored when <paramref name="cursor"/> is supplied, since the cursor already
    /// encodes where the listing left off.
    /// </param>
    /// <param name="cursor">
    /// Opaque, from a previous response's <c>NextCursor</c>.
    /// </param>
    public async Task<Results<Ok<GetModsResponse>, BadRequest<CustomProblemDetails>>> GetAll(
        Guid repoId,
        DateTimeOffset? updatedAfter,
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

        ModsCursor? resumePoint = null;
        if (cursor is not null && !ModsCursor.TryDecode(cursor, out resumePoint))
        {
            return TypedResults.BadRequest(Problems.InvalidCursor(cursor));
        }

        var pageSize = Math.Clamp(limit ?? _defaultLimit, 1, _maximumLimit);

        var query = dbContext.ModVersions.Where(x => x.RepoId == new RepoId(repoId));

        if (resumePoint is not null)
        {
            query = query.Where(x => x.Updated >= resumePoint.Updated);
        }
        else if (updatedAfter is DateTimeOffset since)
        {
            query = query.Where(x => x.Updated > since);
        }

        // Ordering by Updated is what lets the cursor be a timestamp plus a count of the rows
        // already taken at that timestamp: a strongly-typed id has no comparison the provider can
        // translate, so the usual keyset tuple is not available here. It also gives the delta the
        // property it needs — a row written during a listing gets a newer Updated and moves ahead of
        // the cursor, so it may be seen twice but can never be skipped. ModId and Id only make the
        // order total; ties on Updated are common, since one registration stamps every sibling it
        // shifts with the same timestamp.
        var page = await query
            .OrderBy(x => x.Updated).ThenBy(x => x.ModId).ThenBy(x => x.Id)
            .Skip(resumePoint?.Taken ?? 0)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new GetModsResponse(
            page.Select(ModDto.FromModel),
            BuildNextCursor(page, resumePoint, pageSize)));
    }


    private static string? BuildNextCursor(IReadOnlyList<ModVersion> page, ModsCursor? resumePoint, int pageSize)
    {
        if (page.Count < pageSize)
        {
            return null;
        }

        var lastUpdated = page[^1].Updated;
        var takenAtLastUpdated = page.Count(x => x.Updated == lastUpdated);

        // Only carry the previous count forward when the page never left that timestamp, or it would
        // skip rows the page did not actually reach.
        if (resumePoint?.Updated == lastUpdated)
        {
            takenAtLastUpdated += resumePoint.Taken;
        }

        return new ModsCursor(lastUpdated, takenAtLastUpdated).Encode();
    }


    /// <param name="Mods">
    /// One entry per version, with no parent. Nesting versions under a mod would only make the
    /// client re-group on receipt.
    /// </param>
    /// <param name="NextCursor">
    /// <c>null</c> once the listing is exhausted. Note that a delta reports what changed, never what
    /// was deleted — a client that has to notice removals refetches without
    /// <c>updatedAfter</c>.
    /// </param>
    public record GetModsResponse(IEnumerable<ModDto> Mods, string? NextCursor);


    /// <summary>
    /// Where a listing left off: the timestamp of the last row returned, and how many rows carrying
    /// that same timestamp have already been handed out. Opaque to the client so its shape stays a
    /// server concern.
    /// </summary>
    private record ModsCursor(DateTimeOffset Updated, int Taken)
    {
        public string Encode()
        {
            // Ticks rather than a rounded unix timestamp: PostgreSQL keeps microseconds, and a cursor
            // that lands earlier than the row it names would replay rows the page already returned.
            var payload = $"{Updated.UtcTicks.ToString(CultureInfo.InvariantCulture)}|{Taken.ToString(CultureInfo.InvariantCulture)}";

            return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload));
        }

        public static bool TryDecode(string cursor, out ModsCursor? decoded)
        {
            decoded = null;

            if (!Base64Url.IsValid(cursor))
            {
                return false;
            }

            var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor)).Split('|');

            if (parts.Length != 2
                || !long.TryParse(parts[0], CultureInfo.InvariantCulture, out var ticks)
                || ticks < 0 || ticks > DateTimeOffset.MaxValue.UtcTicks
                || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out var taken)
                || taken < 0)
            {
                return false;
            }

            decoded = new ModsCursor(new DateTimeOffset(ticks, TimeSpan.Zero), taken);

            return true;
        }
    }
}
