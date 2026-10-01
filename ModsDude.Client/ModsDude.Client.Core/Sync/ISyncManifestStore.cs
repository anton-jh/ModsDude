using ModsDude.Client.Core.GameAdapters;

namespace ModsDude.Client.Core.Sync;

public interface ISyncManifestStore
{
    /// <returns>
    /// Null when there is none, when it cannot be read, or when it was written by an incompatible
    /// version. All three mean the same thing to a caller - fall back to a full reconcile.
    /// </returns>
    SyncManifest? TryRead(ModTargetRef target);

    /// <summary>
    /// Writes through a temporary file and moves it into place, so an interrupted write leaves the
    /// previous manifest rather than a truncated one.
    /// </summary>
    void Write(SyncManifest manifest);

    /// <summary>
    /// Drops every manifest that is not one of these.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Everything unexpected, not only the targets that have gone.</b> A settings field somebody
    /// emptied, a game disconnected, an adapter author who renamed a key, and a file an older
    /// version wrote under a name nothing builds any more all leave the same thing behind: a
    /// manifest no future read will ever look for. Nothing can tell them apart and nothing needs to -
    /// losing a manifest costs a rescan, which is the whole reason this file is an optimisation
    /// rather than a record.
    /// </para>
    /// <para>
    /// Which is also why a sweep by "what do I still expect" is safe where one by "what has gone"
    /// would not be. It is driven by the persisted target list, so it answers for a game whose
    /// identity no loaded repo serves - and if that list were ever wrong, the cost is a rescan
    /// rather than a folder nobody can account for.
    /// </para>
    /// <para>
    /// Failures are swallowed per file. This runs from a repository constructor at startup, and a
    /// locked manifest is a few hundred kilobytes rather than something worth failing a launch over.
    /// </para>
    /// </remarks>
    void DropStale(IEnumerable<ModTargetRef> expected);

    /// <summary>
    /// What every one of a game's folders agrees it is running, or null where they do not agree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>For the questions asked of the game rather than of one of its folders</b> - which is now
    /// one question: whether a savegame row's two buttons can act here, before anybody has chosen a
    /// slot. Every target of a game follows one profile, so where they all report the same profile
    /// and the same revision there is one answer and the row can state it. Everything keyed on a
    /// binding reads its own target's manifest instead, because the binding says which folder the
    /// save is in.
    /// </para>
    /// <para>
    /// Disagreement - including a target that has never been applied to, and a game reaching no
    /// folder at all - is <b>unknown rather than guessed</b>. It means one folder did not get an
    /// apply that another did, and a row that called the game ready would be offering a check-out
    /// into whichever folder was left behind. Nothing is claimed instead, which reads as "apply the
    /// profile first" - and the apply is per target, so it is also the remedy.
    /// </para>
    /// </remarks>
    SyncManifest? TryReadAgreed(IEnumerable<ModTargetRef> targets);
}
