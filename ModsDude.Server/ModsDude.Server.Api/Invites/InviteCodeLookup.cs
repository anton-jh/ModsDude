using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Domain.Invites;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Invites;

public class InviteCodeLookup(ApplicationDbContext dbContext) : IInviteCodeLookup
{
    public async Task<CodeLookup<TrustCode>> FindTrustCodeAsync(string? input, CancellationToken cancellationToken)
    {
        if (!InviteCodes.TryParse(input, out var code))
        {
            return CodeLookup<TrustCode>.NotFound(Problems.TrustCodeNotFound.With(
                x => x.Detail = "That is not a trust code. Check it against the one you were sent."));
        }

        return await dbContext.TrustCodes.GetByCodeAsync(code, cancellationToken) is TrustCode trustCode
            ? CodeLookup<TrustCode>.Found(trustCode)
            : CodeLookup<TrustCode>.NotFound(Problems.TrustCodeNotFound);
    }

    public async Task<CodeLookup<RepoInviteRedemption>> FindInviteAsync(string? input, CancellationToken cancellationToken)
    {
        if (!InviteCodes.TryParse(input, out var code))
        {
            return CodeLookup<RepoInviteRedemption>.NotFound(Problems.InviteNotFound.With(
                x => x.Detail = "That is not an invite code. Check it against the one you were sent."));
        }

        if (await dbContext.RepoInvites.GetByCodeAsync(code, cancellationToken) is not RepoInvite invite
            || await dbContext.Repos.GetAsync(invite.RepoId, cancellationToken) is not { } repo)
        {
            return CodeLookup<RepoInviteRedemption>.NotFound(Problems.InviteNotFound);
        }

        return CodeLookup<RepoInviteRedemption>.Found(new(repo, invite));
    }
}
