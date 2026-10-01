using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Sync;

public interface IDriftService
{
    /// <param name="activeProfile">
    /// The game's standing intent. Passed rather than read off the game so this depends on
    /// the two facts it actually uses and nothing else.
    /// </param>
    /// <param name="modFolder">
    /// Null where the game reaches no folder at all - somebody connected it and has not filled a
    /// path in. Unknown rather than drifted, like any other unreachable folder.
    /// </param>
    /// <param name="profileIsMissing">
    /// Whether the repo says the active profile is gone. The caller knows; this cannot ask.
    /// </param>
    /// <param name="profileDependencies">
    /// What the profile pins right now, where the caller already had it. Null skips the
    /// profile-changed comparison and leaves the folder check to stand on its own.
    /// </param>
    /// <param name="currentRevision">
    /// Which revision the profile is on now, where the caller knew - the same "where you already had
    /// it" bargain as <paramref name="profileDependencies"/>, and the cheap half of it. Null leaves
    /// the question unasked rather than answered "unchanged".
    /// </param>
    /// <param name="savegameDrift">
    /// What the savegame check found for this <em>game</em>, where the caller ran one - the hold is
    /// the game's and every one of its folders is implicated in it, so a caller checking three
    /// targets hands the same list to each. Carried through
    /// rather than computed here - see <see cref="DriftReport.SavegameDrift"/> - and attached
    /// to <em>every</em> answer including the ones that stop early: a held savegame with an evening in
    /// it is worth saying whatever the mod folder turned out to be, and a game whose profile was
    /// deleted underneath it is precisely a case where somebody wants to hear about their save.
    /// </param>
    DriftReport Check(
        ModTargetRef target,
        ActiveProfile? activeProfile,
        string? modFolder,
        bool profileIsMissing = false,
        IReadOnlyCollection<DesiredMod>? profileDependencies = null,
        int? currentRevision = null,
        IReadOnlyList<Savegames.SavegameDrift>? savegameDrift = null);
}
