using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Profiles;

/// <summary>
/// The mods a profile ignores: read whole, replaced whole.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ignoring is not a revision, and is written on its own.</b> It says nothing about what the
/// profile applies - only which rows the editor's list leaves out until asked - so it neither appears
/// in the history nor has to be atomic with a save. The editor writes it after the save that carried
/// the mod list has succeeded, and not otherwise. See <see cref="ProfileIgnoredMod"/>.
/// </para>
/// <para>
/// <b>A pinned mod cannot also be ignored</b>, and only what the head pins counts. Writing a list that
/// overlaps it is refused, and saving or restoring a revision releases whatever it newly pins, so the
/// two never overlap however the writes interleave.
/// </para>
/// </remarks>
public class GetProfileIgnoredModsV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("repos/{repoId:guid}/profiles/{profileId:guid}/ignoredMods", Get)
            .WithTags("Profiles");
    }


    private static async Task<Results<Ok<ProfileIgnoredModsDto>, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> Get(
        Guid repoId, Guid profileId,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(repoId), RepoMembershipLevel.Guest))
            .MapToForbidden();
        if (authResult is not null)
        {
            return authResult;
        }

        var profile = await dbContext.Profiles.GetAsync(new RepoId(repoId), new ProfileId(profileId), cancellationToken);
        if (profile is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"No profile '{profileId}' found in repo '{repoId}'"));
        }

        var modIds = await dbContext.ProfileIgnoredMods.GetModIdsAsync(profile.RepoId, profile.Id, cancellationToken);

        return TypedResults.Ok(ProfileIgnoredModsDto.From(modIds));
    }
}

/// <inheritdoc cref="GetProfileIgnoredModsV1Endpoint"/>
public class SetProfileIgnoredModsV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPut("repos/{repoId:guid}/profiles/{profileId:guid}/ignoredMods", Set)
            .WithTags("Profiles");
    }


    private static async Task<Results<Ok<ProfileIgnoredModsDto>, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> Set(
        Guid repoId, Guid profileId,
        SetProfileIgnoredModsRequest request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(repoId), RepoMembershipLevel.Member))
            .MapToForbidden();
        if (authResult is not null)
        {
            return authResult;
        }

        var profile = await dbContext.Profiles.GetAsync(new RepoId(repoId), new ProfileId(profileId), cancellationToken);
        if (profile is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"No profile '{profileId}' found in repo '{repoId}'"));
        }

        var desired = request.ModIds.Select(x => new ModId(x)).Distinct().ToList();

        if (desired.Count > ProfileRevisionWrites.MaximumMods)
        {
            return TypedResults.BadRequest(Problems.BatchTooLarge(desired.Count, ProfileRevisionWrites.MaximumMods));
        }

        var overlap = await dbContext.FindPinnedAsync(profile, desired, cancellationToken);

        if (overlap.Count > 0)
        {
            return TypedResults.BadRequest(Problems.IgnoredModPinned(overlap));
        }

        try
        {
            await dbContext.ReplaceIgnoredAsync(profile, desired, cancellationToken);

            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two writes of the same list raced and the primary key let one through. What it wrote is
            // what this was asking for, so a second attempt finds nothing left to add.
            dbContext.ChangeTracker.Clear();

            await dbContext.ReplaceIgnoredAsync(profile, desired, cancellationToken);

            await unitOfWork.CommitAsync(cancellationToken);
        }

        return TypedResults.Ok(ProfileIgnoredModsDto.From(desired));
    }


    /// <param name="ModIds">Everything the profile should ignore. Anything absent stops being ignored.</param>
    public record SetProfileIgnoredModsRequest(IEnumerable<string> ModIds);
}
