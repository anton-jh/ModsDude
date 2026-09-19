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
/// snapshots - so only the check-out half needs recording, and
/// <see cref="SavegameSnapshot.CheckoutId"/> joins the two into one timeline.
/// </para>
/// <para>
/// <b>The claim is advisory.</b> Anybody may take it from anybody, which closes the previous row as
/// <see cref="SavegameCheckoutEndReason.TakenOver"/> and warns naming who held it. What actually
/// protects a save is the base-snapshot check on check-in: the claim is the social half, and only the
/// mechanical half is a guarantee.
/// </para>
/// <para>
/// <b>It does not expire.</b> It used to: a claim lapsed a day after it was last renewed and read as
/// stale. The day was arbitrary - nothing about a savegame becomes free to take after twenty-four hours
/// - so the date it produced meant nothing to whoever read it, and a claim is now held from the moment it
/// is taken until it ends. What a reader needs is who took it and when, which is <see cref="TakenAt"/>;
/// whether that is long enough ago to take it over is theirs to judge, and taking it over is always allowed.
/// </para>
/// </remarks>
public class SavegameCheckout
{
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
    }


    public SavegameCheckoutId Id { get; init; } = new(Guid.NewGuid());

    public RepoId RepoId { get; private set; }
    public SavegameId SavegameId { get; private set; }

    /// <summary>Who took it. Not who may check it in - anybody may.</summary>
    public UserId UserId { get; private set; }

    public DateTime TakenAt { get; private set; }

    /// <summary>Null while this is the open row - which is what "open" means.</summary>
    public DateTime? EndedAt { get; private set; }

    public SavegameCheckoutEndReason? EndedReason { get; private set; }


    public bool IsOpen => EndedAt is null;


    /// <summary>Held while it is the open row, and ended once it is not.</summary>
    public SavegameCheckoutStatus Status => IsOpen
        ? SavegameCheckoutStatus.Held
        : SavegameCheckoutStatus.Ended;

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
    /// <summary>Somebody has it. Since when is <see cref="SavegameCheckout.TakenAt"/>.</summary>
    Held,

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
