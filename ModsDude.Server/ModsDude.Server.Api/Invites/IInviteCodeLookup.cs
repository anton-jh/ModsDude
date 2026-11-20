using ModsDude.Server.Domain.Invites;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Api.Invites;

/// <summary>
/// Finds the trust code or repo invite somebody typed. Used by every place that redeems one, so they
/// all accept and refuse the same input with the same problems.
/// </summary>
public interface IInviteCodeLookup
{
    Task<CodeLookup<TrustCode>> FindTrustCodeAsync(string? input, CancellationToken cancellationToken);

    Task<CodeLookup<RepoInviteRedemption>> FindInviteAsync(string? input, CancellationToken cancellationToken);
}
