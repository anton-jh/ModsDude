using Microsoft.EntityFrameworkCore;

﻿using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Endpoints.Profiles;

public class UpdateProfileV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPut("repos/{repoId:guid}/profiles/{profileId:guid}", Update)
            .WithTags("Profiles")
            .RequireRepoLevel(RepoMembershipLevel.Member);
    }


    private static async Task<Results<Ok<ProfileDto>, BadRequest<CustomProblemDetails>>> Update(
        Guid repoId, Guid profileId,
        UpdateProfileRequest request,
        ApplicationDbContext dbContext,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var profile = await dbContext.Profiles.GetAsync(new RepoId(repoId), new ProfileId(profileId), cancellationToken);
        if (profile is null)
        {
            return TypedResults.BadRequest(Problems.NotFound);
        }

        var name = new ProfileName(request.Name);

        // Already so: a repeat of this request - a retry, a second click - is answered as the first was.
        if (profile.Name == name)
        {
            return TypedResults.Ok(ProfileDto.FromModel(profile));
        }

        if (profile.Version != request.ExpectedVersion)
        {
            return TypedResults.BadRequest(Problems.ProfileChanged);
        }

        if (await dbContext.Profiles.CheckNameIsTaken(new RepoId(repoId), new ProfileId(profileId), name, cancellationToken))
        {
            return TypedResults.BadRequest(Problems.NameTaken(request.Name));
        }

        profile.Rename(name);

        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TypedResults.BadRequest(Problems.ProfileChanged);
        }

        return TypedResults.Ok(ProfileDto.FromModel(profile));
    }


    /// <param name="ExpectedVersion">The version the rename was made against. Another one is refused.</param>
    public record UpdateProfileRequest(string Name, int ExpectedVersion);
}
