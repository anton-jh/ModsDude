using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Repos;

/// <summary>
/// Deletes an archived repo and everything in it - the whole mod catalog, every profile with its
/// history, every savegame with its versions and its claim log, the invites and the memberships.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing inside can refuse it.</b> The rules that stand between a single mod version, profile
/// or savegame and deletion all exist to stop one of them being taken out from under the others -
/// a revision that pins a version, a savegame played on a revision. Deleting the repo takes the
/// dependants and the dependencies together, so there is nothing left to protect and no partial
/// state to protect it from. What makes this safe is not a check on the contents but that a repo
/// can only be deleted once it has been archived, by an Admin: two deliberate acts, and the first
/// one is visible to every member for as long as they care to notice.
/// </para>
/// <para>
/// The blobs are not deleted here - neither the mod files nor the savegame bytes. They are addressed
/// by content and shared between versions, so the reclamation sweep is what removes them once
/// nothing refers to them. The same bargain a deleted mod or savegame already makes.
/// </para>
/// </remarks>
public class DeleteRepoV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapDelete("repo/{repoId:guid}", DeleteRepo)
            .WithTags("Repos");
    }


    private static async Task<Results<Ok, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> DeleteRepo(
        Guid repoId,
        ClaimsPrincipal claimsPrincipal,
        IUnitOfWork unitOfWork,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(repoId), RepoMembershipLevel.Admin))
            .MapToForbidden();
        if (authResult is not null)
        {
            return authResult;
        }

        var repo = await dbContext.Repos.GetAsync(new RepoId(repoId), cancellationToken);
        if (repo is null)
        {
            return TypedResults.BadRequest(Problems.NotFound);
        }

        // Reached from the top-level Archive and nowhere else. A repo carries the group's whole
        // catalog, every profile's history and every savegame, and none of it comes back.
        if (repo.IsArchived is false)
        {
            return TypedResults.BadRequest(Problems.NotArchived("Repo", repoId));
        }

        await DeleteContentsAsync(dbContext, unitOfWork, repo, cancellationToken);

        return TypedResults.Ok();
    }

    /// <summary>
    /// Empties the repo and drops it. <see cref="RepoExtensions.EmptyAsync"/> is where the order the
    /// foreign keys force is written down; the transaction is what keeps the state in between -
    /// contents gone, row still standing - something no other request and no crash can observe.
    /// </summary>
    private static async Task DeleteContentsAsync(
        ApplicationDbContext dbContext,
        IUnitOfWork unitOfWork,
        Repo repo,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await dbContext.EmptyAsync(repo.Id, cancellationToken);

        dbContext.Repos.Remove(repo);
        await unitOfWork.CommitAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}
