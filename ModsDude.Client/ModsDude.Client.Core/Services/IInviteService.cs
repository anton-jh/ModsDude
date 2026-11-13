using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Services;

public interface IInviteService
{
    Task<IReadOnlyList<RepoInviteDto>> GetInvites(Guid repoId, CancellationToken cancellationToken);

    /// <summary>The code of an invite the caller made. Nobody else's can be read.</summary>
    Task<string> GetInviteCode(Guid repoId, Guid inviteId, CancellationToken cancellationToken);

    /// <param name="requestId">The same for a repeat of this request, which then returns the invite it made.</param>
    Task<RepoInviteDto> CreateInvite(
        Guid repoId,
        Guid requestId,
        RepoMembershipLevel level,
        int? maximumUses,
        DateTime? expiresAt,
        CancellationToken cancellationToken);

    Task RevokeInvite(Guid repoId, Guid inviteId, CancellationToken cancellationToken);

    /// <summary>
    /// Joins the repo the code belongs to and puts it in the shell's list, so the caller can navigate
    /// straight to it.
    /// </summary>
    /// <remarks>
    /// Redeeming a code for a repo the user is already in is not an error and does not spend a use;
    /// the server hands back the membership they already had, and this returns it like any other.
    /// </remarks>
    Task<RepoMembershipDto> RedeemInvite(string code, CancellationToken cancellationToken);
}
