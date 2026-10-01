using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Savegames;

/// <summary>
/// Which profile revision a savegame runs on or declares. Shared by the dialogs that preview the
/// answer and the verbs that record it, so the two cannot disagree.
/// </summary>
public static class SavegameRevisionRules
{
    /// <summary>
    /// The revision a savegame pins its mod folder to: null for a current savegame, which follows its
    /// profile's head, and its head snapshot's revision for a past one.
    /// </summary>
    public static int? TargetRevisionOf(SavegameDto savegame)
        // The server refuses a superseded savegame without a profile, so this needs no check for one.
        => savegame.SupersededAt is null ? null : savegame.Head?.ProfileRevision;

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
