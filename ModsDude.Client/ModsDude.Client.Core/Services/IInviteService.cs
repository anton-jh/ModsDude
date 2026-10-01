using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Services;

public interface IInviteService
{
    Task<IReadOnlyList<RepoInviteDto>> GetInvites(Guid repoId, CancellationToken cancellationToken);

    Task<RepoInviteDto> CreateInvite(
        Guid repoId,
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
