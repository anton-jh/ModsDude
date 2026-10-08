namespace ModsDude.Client.Wpf.Shell.Sidebar;

/// <summary>
/// The glyphs the sidebars draw, named once so that the same idea is the same picture everywhere.
/// </summary>
/// <remarks>
/// <para>
/// Segoe Fluent Icons code points, which is the font the rest of the app's iconography already uses -
/// the modals' icon converter, the sidebar headers' add button, the subtle icon buttons. Kept as
/// constants rather than written into the XAML so that the same idea keeps the same glyph wherever it
/// appears, which is what lets somebody find an entry by its shape rather than by reading the column.
/// </para>
/// </remarks>
internal static class MenuIcons
{
    // Rail
    public const string Home = "\xE80F";
    public const string JoinOrCreate = "\xE710";
    public const string Archive = "\xE7B8";
    public const string Settings = "\xE713";

    // Repo
    public const string Overview = "\xE7C3";
    public const string Admin = "\xE7EF";
    public const string Members = "\xE716";
    public const string Mods = "\xE8F1";
    public const string Saves = "\xE74E";
    public const string CreateProfile = "\xE710";
    public const string Game = "\xE7FC";

    // Profile
    public const string History = "\xE81C";

    // The kinds of entity a sidebar names.
    public const string Repo = "\xE8B7";
    public const string Profile = "\xE8FD";

    // Where a profile stands against the game following it. They replace the entity glyph on the one
    // row they are about rather than joining it, so a list of profiles keeps a single column of icons.
    public const string SyncInSync = "\xE930";
    public const string SyncWarning = "\xE7BA";
    public const string SyncApplying = "\xE895";
}
