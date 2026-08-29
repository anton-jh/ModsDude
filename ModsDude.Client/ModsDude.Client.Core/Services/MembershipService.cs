using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Services;

/// <summary>
/// Who is in a repo and at what level. Unlike repos and profiles this holds no live collection: the
/// member list is read by one page and is not part of the shell, so the page owns it.
/// </summary>
public class MembershipService(
    IReposClient reposClient,
    IMembersClient membersClient,
    IUsersClient usersClient)
{
    public async Task<IReadOnlyList<RepoMemberDto>> GetMembers(Guid repoId, CancellationToken cancellationToken)
    {
        var details = await reposClient.GetRepoDetailsV1Async(repoId, cancellationToken);

        return [.. details.Members];
    }

    /// <summary>The user with exactly this username, or null. The server matches the whole name, not a prefix.</summary>
    public async Task<UserDto?> FindUser(string username, CancellationToken cancellationToken)
    {
        var response = await usersClient.SearchUserV1Async(username, cancellationToken);

        return response.User;
    }

    public async Task AddMember(Guid repoId, string userId, RepoMembershipLevel level, CancellationToken cancellationToken)
    {
        var request = new AddMemberRequest()
        {
            UserId = userId,
            MembershipLevel = level
        };

        try
        {
            await membersClient.AddMemberV1Async(repoId, request, cancellationToken);
        }
        catch (ApiException<CustomProblemDetails> ex) when (ex.Result.Type == ProblemType.UserAlreadyMember)
        {
            throw new UserFriendlyException("Already a member", ex.Result.Detail, ex);
        }
        catch (ApiException<CustomProblemDetails> ex) when (ex.Result.Type == ProblemType.NotFound)
        {
            throw new UserFriendlyException("No such user", ex.Result.Detail, ex);
        }
        catch (ApiException<CustomProblemDetails> ex) when (ex.Result.Type == ProblemType.InsufficientRepoAccess)
        {
            throw new UserFriendlyException("You cannot grant that level", ex.Result.Detail, ex);
        }
    }

    public async Task UpdateMembership(Guid repoId, string userId, RepoMembershipLevel level, CancellationToken cancellationToken)
    {
        var request = new UpdateMembershipRequest()
        {
            NewLevel = level
        };

        try
        {
            await membersClient.UpdateMembershipV1Async(repoId, userId, request, cancellationToken);
        }
        catch (ApiException<CustomProblemDetails> ex) when (ex.Result.Type == ProblemType.InsufficientRepoAccess)
        {
            throw new UserFriendlyException("You cannot change that membership", ex.Result.Detail, ex);
        }
    }

    public async Task KickMember(Guid repoId, string userId, CancellationToken cancellationToken)
    {
        try
        {
            await membersClient.KickMemberV1Async(repoId, userId, cancellationToken);
        }
        catch (ApiException<CustomProblemDetails> ex) when (ex.Result.Type == ProblemType.CannotKickOnlyAdmin)
        {
            throw new UserFriendlyException("A repo needs an admin", "The only admin cannot be removed. Promote somebody else first.", ex);
        }
        catch (ApiException<CustomProblemDetails> ex) when (ex.Result.Type == ProblemType.InsufficientRepoAccess)
        {
            throw new UserFriendlyException("You cannot remove that member", ex.Result.Detail, ex);
        }
    }
}
