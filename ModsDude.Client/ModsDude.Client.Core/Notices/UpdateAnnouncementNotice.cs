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
        const string headline = "An update is on its way";

        const string body = "It starts soon and takes a few minutes. Save your changes now: the server goes "
            + "offline, and ModsDude needs a restart afterwards.";

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
