using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One entry in a savegame's history - a version that was minted, or a claim that was taken.
/// </summary>
/// <remarks>
/// <para>
/// <b>One timeline, not two lists.</b> A check-out and the check-in it led to are two halves of the
/// same evening, and reading them side by side in one column is the only way "who had it when, and
/// what came back" is a single question. <see cref="SavegameVersionDto.CheckoutId"/> is what joins
/// them; a version with none was a publish, or a forced check-in taken without a claim.
/// </para>
/// <para>
/// <b>The check-in half is already history</b>, which is why only the check-out half of a claim gets a
/// row: the version <em>is</em> the check-in, and drawing both would say the same thing twice a
/// millimetre apart.
/// </para>
/// </remarks>
public sealed class SavegameTimelineEntryViewModel
{
    private SavegameTimelineEntryViewModel(DateTime moment, string title, string who, string detail)
    {
        Moment = moment;
        Title = title;
        Who = who;
        Detail = detail;
        WhenText = SavegameWording.Exactly(moment);
        AgoText = SavegameWording.Ago(moment);
    }


    /// <summary>The sort key. Everything here is ordered newest first off this one field.</summary>
    public DateTime Moment { get; }

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

    /// <summary>The version behind this row, or null for a check-out row.</summary>
    public SavegameVersionDto? Version { get; private init; }

    public int? VersionNumber => Version?.Number;

    public bool IsVersion => Version is not null;

    /// <summary>Whether this is the version a check-out would take without restoring anything first.</summary>
    public bool IsHead { get; private init; }

    public bool HasLabel => Label is { Length: > 0 };
    public bool HasSize => SizeText is { Length: > 0 };
    public bool HasRevision => RevisionText is { Length: > 0 };


    public static SavegameTimelineEntryViewModel ForVersion(SavegameVersionDto version, bool isHead)
    {
        return new SavegameTimelineEntryViewModel(
            version.Created,
            version.Label is { Length: > 0 } label ? $"Version {version.Number} · {label}" : $"Version {version.Number}",
            version.CreatedBy.DisplayName,
            Describe(version))
        {
            Version = version,
            IsHead = isHead,
            Label = version.Label,
            SizeText = SavegameWording.Size(version.SizeBytes),
            RevisionText = $"Played on revision {version.ProfileRevision}",
            ProfileRevision = version.ProfileRevision
        };
    }

    public static SavegameTimelineEntryViewModel ForCheckout(SavegameCheckoutDto checkout)
    {
        return new SavegameTimelineEntryViewModel(
            checkout.TakenAt,
            "Checked out",
            checkout.User.DisplayName,
            Describe(checkout));
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
    /// What became of a claim. An expired claim is deliberately not an end reason - nothing closed it,
    /// so it is still the open row and reads as stale.
    /// </summary>
    private static string Describe(SavegameCheckoutDto checkout) => (checkout.Status, checkout.EndedReason) switch
    {
        (_, SavegameCheckoutEndReason.CheckedIn) => "Checked back in",
        (_, SavegameCheckoutEndReason.TakenOver) => "Taken over by somebody else",
        (_, SavegameCheckoutEndReason.Discarded) => "Given back without a version",
        (SavegameCheckoutStatus.Stale, _) => $"Still held, and past its expiry on {SavegameWording.OnDate(checkout.ExpiresAt)}",
        _ => "Still held"
    };
}
