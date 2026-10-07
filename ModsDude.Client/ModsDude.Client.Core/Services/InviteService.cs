using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Repos;

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
    IRepoStore repoStore) : IInviteService
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
        // One per call, so a retry of this request returns the invite it made rather than a second one.
        var request = new CreateInviteRequest()
        {
            RequestId = Guid.NewGuid(),
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

    public Task<RepoMembershipDto> RedeemInvite(string code, CancellationToken cancellationToken)
        => repoStore.Join(ct => SendRedeemAsync(code, ct), cancellationToken);

    private async Task<RepoMembershipDto> SendRedeemAsync(string code, CancellationToken cancellationToken)
    {
        var request = new RedeemInviteRequest() { Code = code };

        try
        {
            return await invitesClient.RedeemInviteV1Async(request, cancellationToken);
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
    }
}
