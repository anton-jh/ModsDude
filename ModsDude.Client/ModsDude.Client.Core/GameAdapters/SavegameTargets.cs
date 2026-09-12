using System.Collections;

namespace ModsDude.Client.Core.GameAdapters;

/// <summary>
/// One savegame folder a game reaches on this machine, as the adapter names it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The other half of a target</b>, and keyed on the same <see cref="TargetKey"/> as the
/// <see cref="ModTarget"/> beside it: a save in target T's savegame folder was played against target
/// T's mod folder, and nothing else. That pairing is what lets one apply attribute one folder's
/// evening, and it is expressed as a shared key rather than as a link, because the two halves are
/// answered by two capability adapters that never see each other.
/// </para>
/// <para>
/// <b>Either half may be absent.</b> A target with mods and no saves is an ordinary mod folder; one
/// with saves and no mods is the MP client whose saves live somewhere its mods do not. Neither is a
/// target carrying a null path - the adapter simply omits it from the list it is missing from.
/// </para>
/// </remarks>
/// <param name="Key">
/// The adapter's name for this target, stable across settings edits and adapter versions. It is what
/// a savegame binding records, so an adapter author renaming one leaves this machine holding a save
/// in a folder it can no longer address - see <c>SavegameBindingStore</c>, which keeps the hold
/// rather than dropping it.
/// </param>
/// <param name="DisplayName">
/// What to call this folder where the game has more than one. Null for a game with one, which never
/// mentions it.
/// </param>
/// <param name="Path">The folder itself. It need not exist right now.</param>
public sealed record SavegameTarget(TargetKey Key, string? DisplayName, string Path);


/// <summary>
/// Every savegame folder a game reaches, in the order an adapter wants them read.
/// </summary>
/// <remarks>
/// The same shape as <see cref="ModTargets"/> and for the same reasons - a list rather than a count,
/// derived from the local settings every time, and keys distinct within a game by construction. A
/// game whose settings fill in no savegame folder reaches none, which is an ordinary answer: a
/// mods-only installation is not an error.
/// </remarks>
public sealed class SavegameTargets : IReadOnlyList<SavegameTarget>
{
    /// <summary>A game whose settings point at no savegame folder at all.</summary>
    public static SavegameTargets None { get; } = new();


    private readonly SavegameTarget[] _targets;


    public SavegameTargets(params IEnumerable<SavegameTarget> targets)
    {
        _targets = [.. targets];

        if (_targets.GroupBy(x => x.Key).FirstOrDefault(x => x.Count() > 1) is IGrouping<TargetKey, SavegameTarget> duplicate)
        {
            throw new ArgumentException($"An adapter returned two savegame targets keyed '{duplicate.Key}'.", nameof(targets));
        }
    }


    public int Count => _targets.Length;

    public SavegameTarget this[int index] => _targets[index];

    /// <summary>
    /// The target with this key, or null where the settings no longer produce one.
    /// </summary>
    /// <remarks>
    /// <b>Null is the whole orphan check.</b> A binding records a key, and this is what says whether
    /// the folder behind it still exists - a settings field somebody emptied and an adapter author
    /// renaming a key produce exactly the same null, which is why nothing tries to tell them apart.
    /// </remarks>
    public SavegameTarget? this[TargetKey key] => Array.Find(_targets, x => x.Key == key);


    public IEnumerator<SavegameTarget> GetEnumerator() => ((IEnumerable<SavegameTarget>)_targets).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _targets.GetEnumerator();
}
