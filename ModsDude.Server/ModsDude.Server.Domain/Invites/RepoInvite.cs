using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Invites;

/// <summary>
/// A code that lets whoever holds it join one repo at one level.
/// </summary>
/// <remarks>
/// Joining is an act by the person joining, which is the whole point: nobody can be put into a repo
/// by a stranger who guessed their name, and a user is reachable only by people they gave a code to.
/// The limits are the counterweight - a code shared once travels further than it was meant to, so it
/// can be capped by uses, by time, by both, or left open and revoked when it has served its purpose.
/// </remarks>
/// <remarks>
/// <b>An invite can never grant Admin.</b> A code is a secret that travels, and the whole point of
/// the limits above is the admission that one can end up somewhere it was not meant to go. Everything
/// a leaked Guest or Member code costs is recoverable by an admin - kick the stranger, revoke the
/// code. A leaked Admin code hands over the power to do that, which nothing can take back. Admin is
/// therefore only ever granted deliberately, to a named person who is already in the repo.
/// </remarks>
public class RepoInvite
{
    // ef
    private RepoInvite() { }

    public RepoInvite(
        RepoId repoId,
        InviteCode code,
        RepoMembershipLevel grantedLevel,
        UserId createdBy,
        DateTime created,
        DateTime? expiresAt,
        int? maximumUses)
    {
        if (grantedLevel >= RepoMembershipLevel.Admin)
        {
            throw new DomainValidationException("An invite cannot grant Admin.");
        }

        if (maximumUses is <= 0)
        {
            throw new DomainValidationException($"An invite cannot be limited to {maximumUses} uses.");
        }

        if (expiresAt is DateTime expiry && expiry <= created)
        {
            throw new DomainValidationException("An invite cannot expire before it is created.");
        }

        RepoId = repoId;
        Code = code;
        GrantedLevel = grantedLevel;
        CreatedBy = createdBy;
        Created = created;
        ExpiresAt = expiresAt;
        MaximumUses = maximumUses;
    }


    public RepoInviteId Id { get; init; } = new(Guid.NewGuid());

    public RepoId RepoId { get; private set; }
    public InviteCode Code { get; private set; }
    public RepoMembershipLevel GrantedLevel { get; private set; }
    public UserId CreatedBy { get; private set; }
    public DateTime Created { get; private set; }

    /// <summary>Null where the invite was created without a time limit.</summary>
    public DateTime? ExpiresAt { get; private set; }

    /// <summary>Null where the invite was created without a use limit.</summary>
    public int? MaximumUses { get; private set; }

    /// <summary>Successful joins. A redemption that failed - already a member, revoked - is not one.</summary>
    public int Uses { get; private set; }

    public bool IsRevoked { get; private set; }

    /// <summary>
    /// When somebody took this off the repo's invite list, or null while it is still on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not the same fact as revocation, and not the same fact as being dead.</b> A revoked invite
    /// is dismissed by the same gesture, because "stop this code working" and "take it off my list"
    /// are one act. An <see cref="InviteStatus.Expired"/> or <see cref="InviteStatus.Exhausted"/> one
    /// stays on the list until somebody says otherwise: it is the only evidence that the invite was
    /// made at all, and its absence would read as "I forgot to create one" rather than "it ran out".
    /// </para>
    /// <para>
    /// <b>The row survives.</b> Dismissal hides an invite; it does not delete the record of who came
    /// in through which code, and it does not free the code for reuse - a dismissed invite is still
    /// refused by <see cref="Redeem"/> for whatever reason it was already refused.
    /// </para>
    /// <para>
    /// Repo-level, like every other fact here. Any Member may revoke any invite, so any Member may
    /// dismiss one, and the list is the same list for everybody.
    /// </para>
    /// </remarks>
    public DateTime? DismissedAt { get; private set; }


    /// <summary>
    /// Revocation is reported ahead of the other two because it is the one somebody chose, and the
    /// one they will look for after choosing it.
    /// </summary>
    public InviteStatus GetStatus(DateTime now)
    {
        if (IsRevoked)
        {
            return InviteStatus.Revoked;
        }

        if (ExpiresAt is DateTime expiry && expiry <= now)
        {
            return InviteStatus.Expired;
        }

        if (MaximumUses is int maximum && Uses >= maximum)
        {
            return InviteStatus.Exhausted;
        }

        return InviteStatus.Active;
    }

    public void Redeem(DateTime now)
    {
        if (GetStatus(now) is not InviteStatus.Active)
        {
            throw new InvalidOperationException($"Invite '{Id.Value}' cannot be redeemed while {GetStatus(now)}.");
        }

        Uses++;
    }

    /// <summary>
    /// Irreversible on purpose. An invite is a secret that has been out in the world, and one that
    /// could be switched back on would be a secret nobody could ever finish retiring. Takes it off
    /// the list at the same time - see <see cref="DismissedAt"/>.
    /// </summary>
    public void Revoke(DateTime now)
    {
        IsRevoked = true;

        // Revoking is one gesture, not two. Somebody switching a code off is done with it, and
        // leaving it on the list afterwards means every retired code accumulates there forever.
        DismissedAt = now;
    }

    /// <summary>
    /// Takes a dead invite off the list.
    /// </summary>
    /// <remarks>
    /// <b>Refuses an active one.</b> Hiding a code that still works would leave it live, in the world,
    /// and off the only screen from which it could be revoked. That is the one state this must never
    /// be able to produce, which is why it is checked here and not only at the endpoint.
    /// </remarks>
    public void Dismiss(DateTime now)
    {
        if (GetStatus(now) is InviteStatus.Active)
        {
            throw new DomainValidationException(
                $"Invite '{Id.Value}' still works, so it can be revoked but not dismissed.");
        }

        DismissedAt = now;
    }
}

public readonly record struct RepoInviteId(Guid Value);

public enum InviteStatus
{
    Active,
    Expired,
    Exhausted,
    Revoked
}
