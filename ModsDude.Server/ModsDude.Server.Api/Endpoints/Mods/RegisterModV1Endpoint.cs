using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.EntityTypeConfigurations;
using ModsDude.Server.Persistence.Extensions;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using ModsDude.Server.Persistence.Retention;

namespace ModsDude.Server.Api.Endpoints.Mods;

public class RegisterModV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("repos/{repoId:guid}/mods", RegisterMod)
            .WithTags("Mods")
            .RequireRepoLevel(RepoMembershipLevel.Member);
    }


    public async Task<Results<Ok<ModDto>, BadRequest<CustomProblemDetails>>> RegisterMod(
        Guid repoId,
        RegisterModRequest request,
        IModStorageService storageService,
        ApplicationDbContext dbContext,
        ITimeService timeService,
        IUnitOfWork unitOfWork,
        IRetentionUpkeep retentionUpkeep,
        CancellationToken cancellationToken)
    {
        if (RequestChecks.ModId(request.ModId, request.VersionId) is { } invalidId)
        {
            return TypedResults.BadRequest(invalidId);
        }

        if ((RequestChecks.OptionalVersionId(request.Placement.After) ?? RequestChecks.OptionalVersionId(request.Placement.Before)) is { } invalidPlacement)
        {
            return TypedResults.BadRequest(invalidPlacement);
        }

        var modId = new ModId(request.ModId);
        var versionId = new ModVersionId(request.VersionId);

        // Checked here rather than trusted, because every other member's client writes a file with
        // this name. See ModFileName for what "valid" means and why it is this strict.
        if (!ModFileName.IsValidFor(modId, request.FileName))
        {
            return TypedResults.BadRequest(Problems.InvalidModFileName(modId, request.FileName));
        }

        if (!Sha256Hex.IsValid(request.ContentHash))
        {
            return TypedResults.BadRequest(Problems.InvalidContentHash(request.ContentHash));
        }

        // Metadata is never written for a file nobody has: the blob has to be there first. Its size is read
        // off it while claiming it, rather than being taken from the request - nothing the client says
        // about the bytes can be checked, and this is the one place they are looked at. Claiming is what
        // stops a reclamation sweep that listed the blob as an orphan deleting it from under this
        // registration.
        if (await storageService.ClaimStoredMod(new RepoId(repoId), modId, versionId, cancellationToken) is not { } stored)
        {
            return TypedResults.BadRequest(Problems.ModFileDoesNotExist(new RepoId(repoId), modId, versionId));
        }

        // The hash the uploader recorded as it committed is the only account of the bytes there is. A
        // registration that disagrees with it would send every member's download after a hash the file
        // can never match.
        if (stored.RecordedContentHash != request.ContentHash)
        {
            return TypedResults.BadRequest(Problems.ModContentHashMismatch(new RepoId(repoId), modId, versionId, request.ContentHash, stored.RecordedContentHash));
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
            FileName = request.FileName,
            ContentHash = request.ContentHash,
            SizeBytes = stored.SizeBytes,
            Locked = request.Locked,
            Attributes = new(request.Attributes.Select(ModAttributeDto.ToModel)),
            Created = timestamp,
            Updated = timestamp
        };

        dbContext.ModVersions.Add(modVersion);

        // The checks above read before they write, so a concurrent registration can slip between them.
        // The constraints are what decide, and their refusals are the same outcomes the checks give.
        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation(ModVersionConstraints.PrimaryKey))
        {
            return TypedResults.BadRequest(Problems.ModVersionAlreadyExists(new RepoId(repoId), modId, versionId));
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation(ModVersionConstraints.SequenceIndex) || exception is DbUpdateConcurrencyException)
        {
            return TypedResults.BadRequest(Problems.VersionPlacementConflict(new RepoId(repoId), modId));
        }

        // A new version moves the mod's window, which can take a version winding down out of it.
        await retentionUpkeep.ReleaseModsAsync(modVersion.RepoId, [modVersion.ModId], cancellationToken);

        return TypedResults.Ok(ModDto.FromModel(modVersion));
    }


    /// <param name="FileName">
    /// What the archive is called on the importing machine, extension and all. The mod folder gets
    /// this name verbatim on every machine that applies a profile pinning this version, so it has to
    /// be a bare file name whose stem lower-cases to <paramref name="ModId"/>.
    /// </param>
    public record RegisterModRequest(
        string ModId,
        string VersionId,
        string DisplayName,
        string Description,
        string FileName,
        string ContentHash,
        bool Locked,
        ModVersionPlacement Placement,
        IEnumerable<ModAttributeDto> Attributes);
}
