using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Invites;

/// <summary>
/// A single-use code, issued by the server's admin, that makes whoever redeems it trusted.
/// </summary>
public class TrustCode
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(48);


    // ef
    private TrustCode() { }

    public TrustCode(InviteCode code, TrustCodeRequestId requestId, DateTime created)
    {
        Code = code;
        RequestId = requestId;
        Created = created;
    }


    public TrustCodeId Id { get; init; } = new(Guid.NewGuid());

    public InviteCode Code { get; private set; }
    public TrustCodeRequestId RequestId { get; private set; }
    public DateTime Created { get; private set; }
    public UserId? RedeemedBy { get; private set; }
    public DateTime? RedeemedAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }

    public DateTime ExpiresAt => Created + Lifetime;


    public TrustCodeStatus GetStatus(DateTime now)
    {
        if (RedeemedAt is not null)
        {
            return TrustCodeStatus.Redeemed;
        }

        if (RevokedAt is not null)
        {
            return TrustCodeStatus.Revoked;
        }

        if (ExpiresAt <= now)
        {
            return TrustCodeStatus.Expired;
        }

        return TrustCodeStatus.Active;
    }

    public void Redeem(User user, DateTime now)
    {
        if (GetStatus(now) is not TrustCodeStatus.Active)
        {
            throw new DomainValidationException($"Trust code '{Id.Value}' cannot be redeemed while {GetStatus(now)}.");
        }

        if (user.IsTrusted)
        {
            throw new DomainValidationException($"User '{user.Id.Value}' is already trusted.");
        }

        RedeemedBy = user.Id;
        RedeemedAt = now;
        user.GrantTrust();
    }

    /// <summary>Idempotent. A redeemed code has already done its work and cannot be revoked.</summary>
    public void Revoke(DateTime now)
    {
        if (RedeemedAt is not null)
        {
            throw new DomainValidationException($"Trust code '{Id.Value}' has been redeemed and cannot be revoked.");
        }

        RevokedAt ??= now;
    }
}

public readonly record struct TrustCodeId(Guid Value);

/// <summary>Chosen by the admin page and repeated with a resubmit. Identifies the request, never an entity.</summary>
public readonly record struct TrustCodeRequestId(Guid Value);

public enum TrustCodeStatus
{
    Active,
    Expired,
    Redeemed,
    Revoked
}
