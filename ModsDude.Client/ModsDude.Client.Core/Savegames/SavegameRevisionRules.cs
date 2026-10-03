namespace ModsDude.Client.Core.Savegames;

/// <summary>Which mod list a savegame is checked out or copied onto.</summary>
public enum SavegameRevisionMode
{
    /// <summary>The profile's head, which the savegame then follows.</summary>
    Latest,

    /// <summary>The revision the snapshot was last played on, pinned for as long as it is held.</summary>
    Compatibility
}


/// <summary>
/// Which profile revision a savegame runs on or declares. Shared by the dialogs that preview the
/// answer and the verbs that record it, so the two cannot disagree.
/// </summary>
public static class SavegameRevisionRules
{
    /// <summary>
    /// The revision a check-out pins the mod folder to: null on <see cref="SavegameRevisionMode.Latest"/>,
    /// which follows the profile's head, and the snapshot's own revision in compatibility mode.
    /// </summary>
    /// <param name="playedRevision">The revision the snapshot was played on. Null where it follows no profile.</param>
    public static int? PinnedRevision(SavegameRevisionMode mode, int? playedRevision)
        => mode is SavegameRevisionMode.Compatibility ? playedRevision : null;

    /// <summary>
    /// The revision a published savegame's first snapshot declares: the one the mod folder is on where
    /// that is a revision of the chosen profile, and the profile's head otherwise.
    /// </summary>
    /// <param name="appliedProfileId">What the mod folder was last synced to, from its manifest.</param>
    /// <param name="appliedRevision">Which revision of it.</param>
    public static int DeclaredRevisionFor(
        Guid profileId,
        int headRevision,
        Guid? appliedProfileId,
        int? appliedRevision)
        => profileId == appliedProfileId && appliedRevision is int applied ? applied : headRevision;
}
