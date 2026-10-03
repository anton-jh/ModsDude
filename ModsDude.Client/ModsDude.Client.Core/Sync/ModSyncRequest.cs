using ModsDude.Client.Core.GameAdapters;

namespace ModsDude.Client.Core.Sync;

/// <param name="Target">
/// Which of the game's folders to make match. Sync is genuinely per folder, so this is named by the
/// caller rather than derived here: a game with three targets is three requests, and looping them is
/// what applying a profile does.
/// </param>
/// <param name="Adapter">Already hydrated with the local settings; it is what knows the targets.</param>
public sealed record ModSyncRequest(
    GameIdentity Game,
    string GameName,
    ModTarget Target,
    ILocalModAdapter Adapter,
    Guid RepoId,
    Guid ProfileId)
{
    /// <summary>What the manifest for this run is filed under.</summary>
    public ModTargetRef TargetRef => new(Game, Target.Key);

    /// <summary>
    /// What the profile is called, carried into the manifest so a later drift notice can name it
    /// without a repo's profile list to hand. Optional: sync itself has no use for it.
    /// </summary>
    public string? ProfileName { get; init; }

    /// <summary>
    /// Which revision of the profile to install, or null to let the game's own state decide.
    /// </summary>
    /// <remarks>
    /// <b>Null is the ordinary answer and the one nearly every caller gives.</b> It resolves to the
    /// revision a savegame held here in compatibility mode pins the folder to, and to the profile's head where nothing
    /// pins it - so a re-apply from the drift notice, from the mod list editor and from the game
    /// page all target the right list without any of them knowing what a savegame is. A number is for
    /// the one caller that knows better than the game does: the check-out dialog, previewing the
    /// apply for a savegame this machine is not holding yet.
    /// </remarks>
    public int? Revision { get; init; }

    /// <summary>
    /// Plan against an empty mod list instead of the profile's: everything the adapter recognises in
    /// the folder is taken back out, and the manifest that results describes no profile at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The same engine, pointed at nothing.</b> Clearing a game's mods is exactly what moving it
    /// to a profile that pins none would do, so it is planned, confirmed and executed like any other
    /// apply - recoverable files uninstalled, everything else quarantined - rather than being a
    /// second deletion routine with its own idea of what is safe to delete.
    /// </para>
    /// <para>
    /// <see cref="ProfileId"/> is <see cref="Guid.Empty"/> for one of these, which is what lets the
    /// manifest say the folder is on no profile: nothing can later be activated against it and find
    /// it already matching.
    /// </para>
    /// </remarks>
    public bool ClearAll { get; init; }
}
