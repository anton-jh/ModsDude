using System.Globalization;

namespace ModsDude.Client.Core.Notices;

/// <summary>
/// The notice that an update is on its way: the server goes offline shortly and this build stops
/// working, so the user has a moment to save what they are doing.
/// </summary>
/// <remarks>
/// Pending, and so never a toast. Signed with the announcement's time, so that dismissing it means
/// "not for this one".
/// </remarks>
public static class UpdateAnnouncementNotice
{
    public const string Key = "update/announced";


    public static Notice For(DateTime announcedAt)
    {
        const string headline = "Update starting soon";

        const string body = "The server goes offline for a few minutes. Save your changes now. "
            + "ModsDude needs a restart afterwards.";

        return new Notice(
            Key,
            string.Join("|~|", headline, announcedAt.Ticks.ToString(CultureInfo.InvariantCulture)),
            NoticeSeverity.Pending,
            headline)
        {
            Body = body
        };
    }
}
