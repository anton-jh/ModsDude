namespace ModsDude.Client.Core.Notices;

/// <summary>
/// The notice that a newer version is ready, for the column.
/// </summary>
/// <remarks>
/// <para>
/// <b>Info, and so never a toast.</b> An update waiting is something owed to the app rather than a
/// problem on the disk, and the toast rule is Critical and Warning only - a notification about the app
/// updating itself is the one kind that would be noise on every release.
/// </para>
/// <para>
/// <b>Signed with the version</b>, so that dismissing it means "not for this one": the next release is
/// a different sentence and comes back on its own. Keyed once, because there is only ever one update
/// waiting - a newer download replaces it rather than joining it.
/// </para>
/// </remarks>
public static class UpdateNotice
{
    public const string Key = "update/ready";


    public static Notice For(string version)
    {
        var headline = $"ModsDude {version} is ready to install";

        const string body = "It has been downloaded. Restarting installs it and opens ModsDude again.";

        return new Notice(Key, string.Join("|~|", headline, body), NoticeSeverity.Info, headline)
        {
            Body = body,
            Actions = [new(NoticeActionKind.RestartToUpdate, "Restart now") { IsPrimary = true }]
        };
    }
}
