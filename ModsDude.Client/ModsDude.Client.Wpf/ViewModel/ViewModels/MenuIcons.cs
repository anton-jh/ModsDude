namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// The glyphs the sidebars draw, named once so that the same idea is the same picture everywhere.
/// </summary>
/// <remarks>
/// <para>
/// Segoe Fluent Icons code points, which is the font the rest of the app's iconography already uses -
/// the dialogs' icon converter, the sidebar headers' refresh button, the subtle icon buttons. Kept as
/// constants rather than written into the XAML because several of them appear in more than one
/// sidebar: <b>Saves</b> is an entry under a repo and under an instance, and <b>Manage</b> is one
/// under an instance and under a profile, and those have to be the same glyph or the icons stop being
/// a way to find things.
/// </para>
/// <para>
/// <b>Entities get one too.</b> A repo, a profile and an instance are the rows there are most of, and
/// they are the rows an icon says least about - so they take the plainest glyph of the three kinds,
/// which is enough to keep every row's text starting at the same x and to say which kind of thing a
/// row is when three lists are stacked in one sidebar.
/// </para>
/// </remarks>
internal static class MenuIcons
{
    // Top level
    public const string Home = "\xE80F";
    public const string CreateRepo = "\xE710";
    public const string JoinRepo = "\xE71B";
    public const string Archive = "\xE7B8";
    public const string Settings = "\xE713";

    // Repo
    public const string Overview = "\xE7C3";
    public const string Admin = "\xE7EF";
    public const string Members = "\xE716";
    public const string Mods = "\xE8F1";
    public const string Saves = "\xE74E";
    public const string CreateProfile = "\xE710";
    public const string ConnectGame = "\xE7FC";

    // Profile
    public const string History = "\xE81C";
    public const string Manage = "\xE713";

    // Instance
    public const string Sync = "\xE895";

    // The three kinds of entity a sidebar lists.
    public const string Repo = "\xE8B7";
    public const string Profile = "\xE8FD";
    public const string Instance = "\xE7FC";
}
