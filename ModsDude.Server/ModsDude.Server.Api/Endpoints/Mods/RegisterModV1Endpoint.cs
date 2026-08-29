using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Mods;

public class RegisterModV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("repos/{repoId:guid}/mods", RegisterMod)
            .WithTags("Mods");
    }


    public async Task<Results<Ok<ModDto>, BadRequest<CustomProblemDetails>>> RegisterMod(
        Guid repoId,
        RegisterModRequest request,
        ClaimsPrincipal claimsPrincipal,
        IModStorageService storageService,
        ApplicationDbContext dbContext,
        ITimeService timeService,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(repoId), RepoMembershipLevel.Member))
            .MapToBadRequest();
        if (authResult is not null)
        {
            return authResult;
        }

        var modId = new ModId(request.ModId);
        var versionId = new ModVersionId(request.VersionId);

        // Metadata is never written for a file nobody has: the blob has to be there first.
        if (!await storageService.CheckIfModExists(new RepoId(repoId), modId, versionId, cancellationToken))
        {
            return TypedResults.BadRequest(Problems.ModFileDoesNotExist(new RepoId(repoId), modId, versionId));
        }

        var siblings = await dbContext.ModVersions.GetVersionsOfModAsync(new RepoId(repoId), modId, cancellationToken);

        if (siblings.Any(x => x.Id == versionId))
        {
            return TypedResults.BadRequest(Problems.ModVersionAlreadyExists(new RepoId(repoId), modId, versionId));
        }

        var after = request.Placement.After is null ? (ModVersionId?)null : new ModVersionId(request.Placement.After);
        var before = request.Placement.Before is null ? (ModVersionId?)null : new ModVersionId(request.Placement.Before);

        if (!ModVersionSequencer.CheckPlacementIsValid(siblings, after, before))
        {
            return TypedResults.BadRequest(Problems.VersionPlacementConflict(new RepoId(repoId), modId));
        }

        var timestamp = timeService.Now();

        var modVersion = new ModVersion()
        {
            RepoId = new RepoId(repoId),
            ModId = modId,
            Id = versionId,
            SequenceNumber = ModVersionSequencer.MakeRoomAt(siblings, after, before, timestamp),
            DisplayName = request.DisplayName,
            Description = request.Description,
            ContentHash = request.ContentHash,
            Locked = request.Locked,
            Attributes = new(request.Attributes.Select(ModAttributeDto.ToModel)),
            Created = timestamp,
            Updated = timestamp
        };

        dbContext.ModVersions.Add(modVersion);
        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok(ModDto.FromModel(modVersion));
    }


    public record RegisterModRequest(
        string ModId,
        string VersionId,
        string DisplayName,
        string Description,
        string ContentHash,
        bool Locked,
        ModVersionPlacement Placement,
        IEnumerable<ModAttributeDto> Attributes);

    /// <summary>
    /// Insert the version between these two, both of which are asserted against the ordering as it
    /// stands. The client computes the position with its own adapter's comparer — the server has no
    /// adapters and cannot parse a version string.
    /// </summary>
    public record ModVersionPlacement(string? After, string? Before);
}
