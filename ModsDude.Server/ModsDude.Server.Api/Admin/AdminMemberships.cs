using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Admin;

/// <inheritdoc cref="IAdminMemberships"/>
public class AdminMemberships(
    ApplicationDbContext dbContext,
    IUnitOfWork unitOfWork,
    ILogger<AdminMemberships> logger)
    : IAdminMemberships
{
    private const string _membersChanged = "The repo's members changed. Check them again.";
    private const string _needsAdmin = "A repo needs an Admin. Promote someone else first.";


    public async Task<string?> AddAsync(RepoId repoId, UserId userId, RepoMembershipLevel level, string operatorName, CancellationToken cancellationToken)
    {
        var repo = await dbContext.Repos.GetAsync(repoId, cancellationToken);
        if (repo is null)
        {
            return "That repo no longer exists.";
        }

        var user = await dbContext.Users.GetAsync(userId, cancellationToken);
        if (user is null)
        {
            return "That user no longer exists.";
        }

        if (repo.GetMembership(userId) is { } existing)
        {
            return existing.Level == level ? null : "That user is already a member of that repo.";
        }

        if (user.IsBlocked)
        {
            return "A blocked user cannot be added to a repo.";
        }

        repo.AddMember(user, level);

        if (await CommitAsync(repoId, cancellationToken) is false)
        {
            return _membersChanged;
        }

        logger.LogInformation("Admin {Operator} added user {UserId} to repo {RepoId} as {Level}.",
            operatorName, userId.Value, repoId.Value, level);

        return null;
    }

    public async Task<string?> SetLevelAsync(RepoId repoId, UserId userId, RepoMembershipLevel level, int expectedVersion, string operatorName, CancellationToken cancellationToken)
    {
        var repo = await dbContext.Repos.GetAsync(repoId, cancellationToken);
        if (repo?.GetMembership(userId) is not { } membership)
        {
            return "That user is no longer a member of that repo.";
        }

        if (membership.Level == level)
        {
            return null;
        }

        if (repo.MembersVersion != expectedVersion)
        {
            return _membersChanged;
        }

        if (level < RepoMembershipLevel.Admin && repo.IsOnlyAdmin(userId))
        {
            return _needsAdmin;
        }

        var previous = membership.Level;
        repo.UpdateMembershipLevel(userId, level);

        if (await CommitAsync(repoId, cancellationToken) is false)
        {
            return _membersChanged;
        }

        logger.LogInformation("Admin {Operator} changed user {UserId} in repo {RepoId} from {Previous} to {Level}.",
            operatorName, userId.Value, repoId.Value, previous, level);

        return null;
    }

    public async Task<string?> RemoveAsync(RepoId repoId, UserId userId, int expectedVersion, string operatorName, CancellationToken cancellationToken)
    {
        var repo = await dbContext.Repos.GetAsync(repoId, cancellationToken);
        if (repo?.GetMembership(userId) is null)
        {
            return null;
        }

        if (repo.MembersVersion != expectedVersion)
        {
            return _membersChanged;
        }

        if (repo.IsOnlyAdmin(userId))
        {
            return _needsAdmin;
        }

        repo.KickMember(userId);

        if (await CommitAsync(repoId, cancellationToken) is false)
        {
            return _membersChanged;
        }

        logger.LogInformation("Admin {Operator} removed user {UserId} from repo {RepoId}.",
            operatorName, userId.Value, repoId.Value);

        return null;
    }


    /// <returns>False where another change to the repo's members was saved first.</returns>
    private async Task<bool> CommitAsync(RepoId repoId, CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.CommitAsync(cancellationToken);

            return true;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogInformation(exception, "An admin membership change in repo {RepoId} lost to a concurrent change.", repoId.Value);
            dbContext.ChangeTracker.Clear();

            return false;
        }
    }
}
