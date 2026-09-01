using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Services;

/// <summary>
/// The codes that let somebody into a repo, and the one way in.
/// </summary>
/// <remarks>
/// Holds no live collection: invites are read by the page that manages them, and nothing else in the
/// shell is built from them.
/// </remarks>
public class InviteService(
    IInvitesClient invitesClient,
    RepoRepository repoRepository)
{
    public async Task<IReadOnlyList<RepoInviteDto>> GetInvites(Guid repoId, CancellationToken cancellationToken)
    {
        return [.. await invitesClient.GetInvitesV1Async(repoId, cancellationToken)];
    }

    public async Task<RepoInviteDto> CreateInvite(
        Guid repoId,
        RepoMembershipLevel level,
        int? maximumUses,
        DateTime? expiresAt,
        CancellationToken cancellationToken)
    {
        var request = new CreateInviteRequest()
        {
            MembershipLevel = level,
            MaximumUses = maximumUses,
            ExpiresAt = expiresAt
        };

        try
        {
            return await invitesClient.CreateInviteV1Async(repoId, request, cancellationToken);
        }
        catch (ApiException<CustomProblemDetails> ex) when (ex.Result.Type == ProblemType.InsufficientRepoAccess)
        {
            throw new UserFriendlyException("You cannot invite at that level", ex.Result.Detail, ex);
        }
        catch (ApiException<CustomProblemDetails> ex) when (ex.Result.Type == ProblemType.InvalidInviteLimits)
        {
            throw new UserFriendlyException("Those limits do not work", ex.Result.Detail, ex);
        }
        catch (ApiException<CustomProblemDetails> ex) when (ex.Result.Type == ProblemType.InviteCannotGrantAdmin)
        {
            // Unreachable from the app, which does not offer Admin in the picker. Mapped anyway,
            // because the rule lives on the server and this is what it says when it is broken.
            throw new UserFriendlyException("An invite cannot grant Admin", ex.Result.Detail, ex);
        }
    }

    public async Task RevokeInvite(Guid repoId, Guid inviteId, CancellationToken cancellationToken)
    {
        try
        {
            await invitesClient.RevokeInviteV1Async(repoId, inviteId, cancellationToken);
        }
        catch (ApiException<CustomProblemDetails> ex) when (ex.Result.Type == ProblemType.InsufficientRepoAccess)
        {
            throw new UserFriendlyException("You cannot manage this repo's invites", ex.Result.Detail, ex);
        }
    }

    /// <summary>
    /// Joins the repo the code belongs to and puts it in the shell's list, so the caller can navigate
    /// straight to it.
    /// </summary>
    /// <remarks>
    /// Redeeming a code for a repo the user is already in is not an error and does not spend a use;
    /// the server hands back the membership they already had, and this returns it like any other.
    /// </remarks>
    public async Task<RepoMembershipDto> RedeemInvite(string code, CancellationToken cancellationToken)
    {
        var request = new RedeemInviteRequest() { Code = code };

        RepoMembershipDto membership;

        try
        {
            membership = await invitesClient.RedeemInviteV1Async(request, cancellationToken);
        }
        catch (ApiException<CustomProblemDetails> ex) when (ex.Result.Type == ProblemType.InviteNotFound)
        {
            throw new UserFriendlyException("No such invite", ex.Result.Detail, ex);
        }
        catch (ApiException<CustomProblemDetails> ex) when (ex.Result.Type == ProblemType.InviteNotUsable)
        {
            throw new UserFriendlyException("That invite no longer works", ex.Result.Detail, ex);
        }
        catch (ApiException<CustomProblemDetails> ex) when (ex.Result.Type == ProblemType.InviteRedemptionConflict)
        {
            throw new UserFriendlyException("Try that again", ex.Result.Detail, ex);
        }

        repoRepository.AddJoinedRepo(membership);

        return membership;
    }
}
