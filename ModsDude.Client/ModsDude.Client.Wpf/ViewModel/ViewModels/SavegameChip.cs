using System.Globalization;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// How loud a chip is. Three tones rather than a colour, so the decision about what deserves alarm is
/// made once, here, rather than in every template that draws one.
/// </summary>
public enum SavegameChipTone
{
    /// <summary>A fact. Somebody else holds it, the save is available, the version is old.</summary>
    Neutral,

    /// <summary>A fact about <em>you</em> - what you are holding right now.</summary>
    Accent,

    /// <summary>
    /// Something that can damage a save. Reserved for a locked pin having moved and for play that
    /// exists nowhere but this machine; spending it on ordinary staleness is what teaches people to
    /// ignore it.
    /// </summary>
    Caution
}


/// <summary>One chip on a savegame row: a short sentence and how loudly to say it.</summary>
public sealed record SavegameChip(string Text, SavegameChipTone Tone)
{
    public bool IsCaution => Tone is SavegameChipTone.Caution;
    public bool IsAccent => Tone is SavegameChipTone.Accent;
    public bool IsNeutral => Tone is SavegameChipTone.Neutral;
}


/// <summary>
/// The words this feature says about time, size and playtime, in one place.
/// </summary>
/// <remarks>
/// A held claim reads as an elapsed time and a stale one as a date, and that difference is the whole
/// point: "since 20 minutes ago" is somebody who is playing, "since 3 March" is somebody who forgot.
/// The wording is what carries the distinction, so both live here rather than being formatted at each
/// call site.
/// </remarks>
public static class SavegameWording
{
    /// <summary>
    /// How long ago something happened, in the roundest form that is still true. Falls back to a date
    /// once "days ago" stops meaning anything.
    /// </summary>
    public static string Ago(DateTime moment)
    {
        var elapsed = DateTime.UtcNow - DateTime.SpecifyKind(moment, DateTimeKind.Utc);

        if (elapsed < TimeSpan.Zero)
        {
            // A clock that disagrees with the server's is not worth a sentence of its own.
            return "just now";
        }

        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            var minutes = (int)elapsed.TotalMinutes;

            return minutes == 1 ? "a minute ago" : $"{minutes} minutes ago";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            var hours = (int)elapsed.TotalHours;

            return hours == 1 ? "an hour ago" : $"{hours} hours ago";
        }

        if (elapsed < TimeSpan.FromDays(7))
        {
            var days = (int)elapsed.TotalDays;

            return days == 1 ? "yesterday" : $"{days} days ago";
        }

        return OnDate(moment);
    }

    /// <summary>The day something happened, for anything too old to count in hours.</summary>
    public static string OnDate(DateTime moment)
        => moment.ToLocalTime().ToString("d MMMM", CultureInfo.CurrentCulture);

    /// <summary>Date and time, for a detail panel where the exact moment is the point.</summary>
    public static string Exactly(DateTime moment)
        => moment.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    /// <summary>A blob size in the units somebody thinks about a savegame in.</summary>
    public static string Size(long bytes) => ByteSize.Describe(bytes);

    /// <summary>
    /// How long a save has been played, as the game recorded it. Null where the game does not say -
    /// which is not the same as zero, so nothing is shown rather than "0 min".
    /// </summary>
    public static string? Playtime(TimeSpan? playtime)
    {
        if (playtime is not TimeSpan value || value <= TimeSpan.Zero)
        {
            return null;
        }

        var hours = (int)value.TotalHours;

        return hours == 0
            ? $"{value.Minutes} min played"
            : $"{hours} h {value.Minutes:00} min played";
    }

    /// <summary>"2 revisions behind", or "1 revision behind" - never "1 revisions behind".</summary>
    public static string RevisionsBehind(int count)
        => count == 1 ? "1 revision behind" : $"{count} revisions behind";
}
