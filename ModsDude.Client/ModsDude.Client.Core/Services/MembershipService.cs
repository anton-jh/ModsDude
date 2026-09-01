using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Services;

/// <summary>
/// Who is in a repo and at what level. Unlike repos and profiles this holds no live collection: the
/// member list is read by one page and is not part of the shell, so the page owns it.
/// </summary>
/// <remarks>
/// Nothing here adds anybody. A member arrives by redeeming an invite - see
/// <see cref="InviteService"/> - so what is left to do to one is change their level or remove them.
/// </remarks>
public class MembershipService(
    IReposClient reposClient,
    IMembersClient membersClient)
{
    public async Task<IReadOnlyList<RepoMemberDto>> GetMembers(Guid repoId, CancellationToken cancellationToken)
    {
        var details = await reposClient.GetRepoDetailsV1Async(repoId, cancellationToken);

        return [.. details.Members];
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
