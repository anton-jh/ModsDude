using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// Where a row sits among the rows sharing its stamp.
/// </summary>
/// <remarks>
/// <para>
/// Publishing, checking in and taking a save over each write two of these in one transaction off a
/// single clock reading, so the moment alone cannot order them - and left to itself the sort falls
/// to whichever of the two reads happened to be concatenated first, which is how a save came to look
/// as though it had been checked out before it existed.
/// </para>
/// <para>
/// One order covers all three, which is why this is a rank and not a special case per event.
/// Earliest to latest inside an instant: a claim ends, a version is minted, a claim is taken.
/// Publishing mints the first version and then hands the publisher the claim; a check-in closes the
/// claim and then mints what it produced; a take-over closes the old claim and then opens the new
/// one.
/// </para>
/// </remarks>
public enum SavegameTimelineRank
{
    ClaimEnded,
    Version,
    ClaimTaken
}


/// <summary>
/// One entry in a savegame's history - a version that was minted, a claim that was taken, or a claim
/// that ended.
/// </summary>
/// <remarks>
/// <para>
/// <b>One timeline, not two lists.</b> Versions and claims are read from two logs on the server and
/// merged into one column here, because "who had it when, and what came back" is a single question.
/// </para>
/// <para>
/// <b>Taking a save and handing it back are two rows</b>, at the two moments they happened, because
/// they are two evenings apart as often as not - one row carrying both would date the whole thing to
/// whichever of the two it chose. What ties them back together is <see cref="IsClosed"/>, which
/// strikes the check-out through once the claim behind it is over.
/// </para>
/// <para>
/// <b>A claim that a version already records gets no ending row.</b> An ordinary check-in mints a
/// version stamped with <see cref="SavegameVersionDto.CheckoutId"/>, and that version <em>is</em> the
/// check-in - drawing a thin "Checked back in" a millimetre under it would say the same thing twice
/// at the same second. The ending row is for the endings nothing else records: a check-in whose bytes
/// matched the head and so minted nothing, a claim given back without a version, a save taken over,
/// and a check-in whose version has since been pruned.
/// </para>
/// </remarks>
public sealed class SavegameTimelineEntryViewModel
{
    private SavegameTimelineEntryViewModel(
        DateTime moment,
        SavegameTimelineRank rank,
        string title,
        string who,
        string detail)
    {
        Moment = moment;
        Rank = rank;
        Title = title;
        Who = who;
        Detail = detail;
        WhenText = SavegameWording.Exactly(moment);
        AgoText = SavegameWording.Ago(moment);
    }


    /// <summary>The sort key. Everything here is ordered newest first off this field, then by <see cref="Rank"/>.</summary>
    public DateTime Moment { get; }

    /// <inheritdoc cref="SavegameTimelineRank"/>
    public SavegameTimelineRank Rank { get; }

    public string Title { get; }
    public string Who { get; }
    public string Detail { get; }
    public string WhenText { get; }
    public string AgoText { get; }

    public string? Label { get; private init; }
    public string? SizeText { get; private init; }

    /// <summary>The profile revision this version was played on. The recorded truth, not what the save file believes.</summary>
    public string? RevisionText { get; private init; }

    public int? ProfileRevision { get; private init; }

    /// <summary>The version behind this row, or null for either of the two claim rows.</summary>
    public SavegameVersionDto? Version { get; private init; }

    public int? VersionNumber => Version?.Number;

    public bool IsVersion => Version is not null;

    /// <summary>
    /// The other half of <see cref="IsVersion"/>, and what the list draws a thin row for. A version is
    /// something the savegame still has; a claim row is something that merely happened to it, and the
    /// two reading alike is what made the history hard to skim.
    /// </summary>
    public bool IsEvent => Version is null;

    /// <summary>
    /// Whether the claim this row opened is over. Only ever true on a
    /// <see cref="SavegameTimelineRank.ClaimTaken"/> row, where it strikes the title through - which is
    /// what lets the check-out keep its own date without pretending it is still live.
    /// </summary>
    public bool IsClosed { get; private init; }

    /// <summary>Whether this is the version a check-out would take without restoring anything first.</summary>
    public bool IsHead { get; private init; }

    public bool HasLabel => Label is { Length: > 0 };
    public bool HasSize => SizeText is { Length: > 0 };
    public bool HasRevision => RevisionText is { Length: > 0 };
    public bool HasDetail => Detail is { Length: > 0 };

    /// <summary>
    /// What the adapter recorded about this version, in its own order. Empty for a claim row, and for
    /// a version checked in by a client whose adapter describes nothing.
    /// </summary>
    public IReadOnlyList<SavegameDetailDto> Details { get; private init; } = [];

    public bool HasDetails => Details.Count > 0;


    public static SavegameTimelineEntryViewModel ForVersion(SavegameVersionDto version, bool isHead)
    {
        return new SavegameTimelineEntryViewModel(
            version.Created,
            SavegameTimelineRank.Version,
            version.Label is { Length: > 0 } label ? $"Version {version.Number} · {label}" : $"Version {version.Number}",
            version.CreatedBy.DisplayName,
            Describe(version))
        {
            Version = version,
            IsHead = isHead,
            Label = version.Label,
            SizeText = SavegameWording.Size(version.SizeBytes),
            RevisionText = $"Played on revision {version.ProfileRevision}",
            Details = [.. version.Details],
            ProfileRevision = version.ProfileRevision
        };
    }

    /// <summary>
    /// The moment somebody took the save. It keeps that date whatever became of the claim afterwards;
    /// the fate is <see cref="IsClosed"/>, and the row that ended it.
    /// </summary>
    public static SavegameTimelineEntryViewModel ForClaimTaken(SavegameCheckoutDto checkout)
    {
        var closed = checkout.EndedAt is not null;

        return new SavegameTimelineEntryViewModel(
            checkout.TakenAt,
            SavegameTimelineRank.ClaimTaken,
            "Checked out",
            checkout.User.DisplayName,
            // Nothing under a struck-through check-out: the strike says the claim is over, and what
            // ended it is its own row further up rather than a second sentence down here.
            closed ? "" : DescribeHold(checkout))
        {
            IsClosed = closed
        };
    }

    /// <summary>
    /// The moment a claim ended. Only built for claims no version records - see the remarks on the
    /// type for which endings those are.
    /// </summary>
    public static SavegameTimelineEntryViewModel ForClaimEnded(SavegameCheckoutDto checkout)
    {
        var (title, detail) = DescribeEnd(checkout.EndedReason);

        return new SavegameTimelineEntryViewModel(
            // The caller only builds this for a claim that ended. The fallback is so a stray one
            // lands somewhere truthful rather than throwing in the middle of a list build.
            checkout.EndedAt ?? checkout.TakenAt,
            SavegameTimelineRank.ClaimEnded,
            title,
            checkout.User.DisplayName,
            detail);
    }


    /// <summary>
    /// What a version was. A forced check-in and a restore both name what they were built on, because
    /// that is the only place the fork shows up without anybody having to draw a tree.
    /// </summary>
    private static string Describe(SavegameVersionDto version) => version.Origin switch
    {
        SavegameVersionOrigin.Created => "Published",
        SavegameVersionOrigin.CheckedIn => "Checked in",
        SavegameVersionOrigin.Forced => version.BaseVersion is int forced
            ? $"Forced in over version {forced}, which stays in the history"
            : "Forced in over a newer version, which stays in the history",
        SavegameVersionOrigin.Restored => version.BaseVersion is int restored
            ? $"Restored version {restored}"
            : "Restored an earlier version",
        _ => ""
    };

    /// <summary>
    /// What an open claim is. An expired one is deliberately not an ending - nothing closed it, so it
    /// is still the open row and reads as stale rather than as something that happened.
    /// </summary>
    private static string DescribeHold(SavegameCheckoutDto checkout)
        => checkout.Status is SavegameCheckoutStatus.Stale
            ? $"Still held, and past its expiry on {SavegameWording.OnDate(checkout.ExpiresAt)}"
            : "Still held";

    /// <summary>
    /// How a claim ended. <see cref="Who"/> on this row is whoever held it, which is also whoever
    /// acted - except for a take-over, where the person who acted is named by the check-out sitting
    /// directly above it at the same second.
    /// </summary>
    private static (string Title, string Detail) DescribeEnd(SavegameCheckoutEndReason? reason) => reason switch
    {
        SavegameCheckoutEndReason.CheckedIn => ("Checked back in", ""),
        SavegameCheckoutEndReason.TakenOver => ("Taken over", "Somebody else took the save"),
        SavegameCheckoutEndReason.Discarded => ("Given back", "Without a version"),
        _ => ("Handed back", "")
    };
}
