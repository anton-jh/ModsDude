namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// The glyphs the sidebars draw, named once so that the same idea is the same picture everywhere.
/// </summary>
/// <remarks>
/// <para>
/// Segoe Fluent Icons code points, which is the font the rest of the app's iconography already uses -
/// the dialogs' icon converter, the sidebar headers' refresh button, the subtle icon buttons. Kept as
/// constants rather than written into the XAML so that the same idea keeps the same glyph wherever it
/// appears, which is what lets somebody find an entry by its shape rather than by reading the column.
/// </para>
/// <para>
/// <b>Two sidebars, not three.</b> There is no game sidebar any more - a local installation is a
/// settings entry under its repo rather than a shell of its own - so <b>Saves</b> and <b>Manage</b>
/// each appear once. <see cref="Game"/> and <see cref="ConnectGame"/> are deliberately the same
/// glyph: they are the two states of one entry, and it would flicker between shapes as a game is
/// connected and disconnected otherwise.
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

    // The kinds of entity a sidebar names. Game is the repo menu's settings entry rather than a row
    // in a list of its own, and shares ConnectGame's glyph because the two are one entry's two states.
    public const string Repo = "\xE8B7";
    public const string Profile = "\xE8FD";
    public const string Game = "\xE7FC";
}
