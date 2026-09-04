using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Savegames;

/// <summary>
/// One person's claim on one savegame, from the moment they took it to the moment it ended.
/// </summary>
/// <remarks>
/// <para>
/// <b>A log, not a field.</b> The current holder is the row that has not ended, which is why there is
/// no <c>Checkout</c> property on <see cref="Savegame"/> to keep in step with a history beside it. A
/// filtered unique index permits one open row per savegame. Check-ins are already history - they are
/// versions - so only the check-out half needs recording, and
/// <see cref="SavegameVersion.CheckoutId"/> joins the two into one timeline.
/// </para>
/// <para>
/// <b>The claim is advisory.</b> Anybody may take it from anybody, which closes the previous row as
/// <see cref="SavegameCheckoutEndReason.TakenOver"/> and warns naming who held it. What actually
/// protects a save is the base-version check on check-in: the claim is the social half, and only the
/// mechanical half is a guarantee.
/// </para>
/// <para>
/// <b>It expires, and is renewed while it is held.</b> Somebody who takes a save on Friday and goes
/// on holiday has to read as stale rather than as holding it - a warning that never clears is a
/// warning everybody learns to click past.
/// </para>
/// </remarks>
public class SavegameCheckout
{
    /// <summary>
    /// How long a fresh claim stands without being renewed. Long enough that a session, a meal and an
    /// evening do not expire it; short enough that a forgotten claim stops shouting by the next day.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);


    // ef
    private SavegameCheckout() { }

    public SavegameCheckout(
        RepoId repoId,
        SavegameId savegameId,
        UserId userId,
        DateTime takenAt)
    {
        RepoId = repoId;
        SavegameId = savegameId;
        UserId = userId;
        TakenAt = takenAt;
        ExpiresAt = takenAt + Lifetime;
    }


    public SavegameCheckoutId Id { get; init; } = new(Guid.NewGuid());

    public RepoId RepoId { get; private set; }
    public SavegameId SavegameId { get; private set; }

    /// <summary>Who took it. Not who may check it in - anybody may.</summary>
    public UserId UserId { get; private set; }

    public DateTime TakenAt { get; private set; }

    /// <summary>
    /// When the claim stops reading as held. Pushed forward by <see cref="Renew"/> while the holder
    /// still has the app open, so a live claim never goes stale under somebody who is playing.
    /// </summary>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>Null while this is the open row - which is what "open" means.</summary>
    public DateTime? EndedAt { get; private set; }

    public SavegameCheckoutEndReason? EndedReason { get; private set; }


    public bool IsOpen => EndedAt is null;


    /// <summary>
    /// Ended is reported ahead of expiry because it is the one that actually happened: a claim that
    /// was checked in yesterday is ended, not stale, however long ago it was due to expire.
    /// </summary>
    public SavegameCheckoutStatus GetStatus(DateTime now)
    {
        if (!IsOpen)
        {
            return SavegameCheckoutStatus.Ended;
        }

        return ExpiresAt <= now
            ? SavegameCheckoutStatus.Stale
            : SavegameCheckoutStatus.Held;
    }

    /// <summary>
    /// Pushes the claim's expiry out from <paramref name="now"/>. Renewing a claim that has already
    /// gone stale is deliberately allowed: the holder coming back is exactly the case, and refusing
    /// it would force them to take their own save off themselves.
    /// </summary>
    public void Renew(DateTime now)
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException($"Checkout '{Id.Value}' has ended and cannot be renewed.");
        }

        ExpiresAt = now + Lifetime;
    }

    public void End(DateTime now, SavegameCheckoutEndReason reason)
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException($"Checkout '{Id.Value}' has already ended.");
        }

        EndedAt = now;
        EndedReason = reason;
    }
}


public readonly record struct SavegameCheckoutId(Guid Value);


/// <summary>What a claim looks like to somebody reading the savegame list.</summary>
public enum SavegameCheckoutStatus
{
    /// <summary>Somebody has it, and has had the app open recently enough to say so.</summary>
    Held,

    /// <summary>
    /// Open, but nobody has renewed it. Read as "Anton has had this since 3 March" rather than as
    /// "Anton has this", because the two mean very different things to whoever wants to play.
    /// </summary>
    Stale,

    Ended
}


public enum SavegameCheckoutEndReason
{
    /// <summary>The holder checked the save back in, which is the ordinary end.</summary>
    CheckedIn,

    /// <summary>Somebody else took the save while this claim was open.</summary>
    TakenOver,

    /// <summary>The holder gave it back without checking anything in - taken by mistake, never played.</summary>
    Discarded
}
