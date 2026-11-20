using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Invites;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Domain.Users;

/// <summary>
/// The only way a user comes into existence: with a trust code, at least one repo invite, or both.
/// Every code is redeemed, or the sign-up is refused.
/// </summary>
public static class UserSignUp
{
    public static User Create(
        UserId id,
        DisplayName displayName,
        DateTime now,
        TrustCode? trustCode,
        IReadOnlyList<RepoInviteRedemption> invites)
    {
        if (trustCode is null && invites.Count == 0)
        {
            throw new DomainValidationException("A user signs up with a trust code or at least one repo invite.");
        }

        if (invites.Select(x => x.Repo.Id).Distinct().Count() != invites.Count)
        {
            throw new DomainValidationException("A user cannot sign up with two invites to the same repo.");
        }

        var user = new User(id, displayName, now);

        trustCode?.Redeem(user, now);

        foreach (var (repo, invite) in invites)
        {
            repo.Join(user, invite, now);
        }

        return user;
    }
}

public sealed record RepoInviteRedemption(Repo Repo, RepoInvite Invite);
