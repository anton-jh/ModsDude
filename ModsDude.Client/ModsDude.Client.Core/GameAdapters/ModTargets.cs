using ModsDude.Client.Core.Exceptions;
using System.Collections;

namespace ModsDude.Client.Core.GameAdapters;

/// <summary>
/// One mod folder a game reaches on this machine, as the adapter names it.
/// </summary>
/// <remarks>
/// <b>A value the adapter returns, not a persisted entity.</b> No id, no row, no list the user
/// manages: a game that needs more than one target says so in its <c>LocalSettings</c>, and emptying
/// a field there takes the target away again.
/// </remarks>
/// <param name="Key">
/// The adapter's name for this target, stable across settings edits and adapter versions. It ends up
/// in a filename, which is the store's business to encode rather than a rule this has to obey - but
/// it is also what a manifest and a savegame binding are keyed on, so an adapter author renaming one
/// orphans both and is indistinguishable from having removed the target.
/// </param>
/// <param name="DisplayName">
/// What to call this folder where the game has more than one - "dedicated server", "MP client". Null
/// for a game with one target, which never mentions it: Farming Simulator has a mod folder, not a
/// mod folder called something.
/// </param>
/// <param name="Path">The folder itself. It need not exist right now.</param>
public sealed record ModTarget(TargetKey Key, string? DisplayName, string Path);


/// <summary>
/// An adapter's name for one of a game's targets. Opaque outside the adapter that minted it.
/// </summary>
/// <remarks>
/// A type rather than a bare string for the reason <see cref="GameIdentity"/> is one: a target key, a
/// slot id and a folder name are all plausible-looking strings, and putting the wrong one in a
/// lookup fails as a silently empty answer rather than as a compile error.
/// </remarks>
public readonly record struct TargetKey
{
    /// <summary>
    /// Reserved because <see cref="Models.SavegameSlotRef"/> renders as <c>{target}:{slot}</c>, and a
    /// key carrying the separator would make that rendering ambiguous to read back.
    /// </summary>
    internal const string Separator = ":";


    public TargetKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A target key cannot be blank.", nameof(value));
        }
        if (value.Contains(Separator))
        {
            throw new ArgumentException($"A target key cannot contain the separator: '{Separator}'", nameof(value));
        }

        Value = value;
    }


    public string Value { get; }


    public override string ToString() => Value;
}


/// <summary>
/// Every mod folder a game reaches, in the order an adapter wants them read.
/// </summary>
/// <remarks>
/// <para>
/// <b>A list, never a count.</b> Farming Simulator has one and BeamNG.drive with BeamMP has three;
/// both are this. A blank folder in an adapter's <c>LocalSettings</c> is a target the adapter omits
/// rather than one with a null path, so a game can reach none at all.
/// </para>
/// <para>
/// Keys are distinct within a game - a construction, checked here, rather than something every
/// adapter has to be trusted with.
/// </para>
/// </remarks>
public sealed class ModTargets : IReadOnlyList<ModTarget>
{
    /// <summary>A game whose settings point at no folder at all. Nothing to sync, and not an error.</summary>
    public static ModTargets None { get; } = new();


    private readonly ModTarget[] _targets;


    public ModTargets(params IEnumerable<ModTarget> targets)
    {
        _targets = [.. targets];

        if (_targets.GroupBy(x => x.Key).FirstOrDefault(x => x.Count() > 1) is IGrouping<TargetKey, ModTarget> duplicate)
        {
            throw new ArgumentException($"An adapter returned two targets keyed '{duplicate.Key}'.", nameof(targets));
        }
    }


    public int Count => _targets.Length;

    public ModTarget this[int index] => _targets[index];

    /// <summary>The target with this key, or null where the settings no longer produce one.</summary>
    public ModTarget? this[TargetKey key] => Array.Find(_targets, x => x.Key == key);


    /// <summary>
    /// The one target, or none where the settings point at no folder at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Several is the tripwire; none is data.</b> A game reaching no mod folder is an ordinary
    /// answer - somebody connected it and has not filled a path in - and a caller that can say
    /// something sensible about that gets to. A game reaching three is a caller that was written when
    /// every game had one, and taking the first would work perfectly for Farming Simulator while
    /// silently leaving two thirds of a BeamNG game on the old mod list.
    /// </para>
    /// <para>
    /// Scaffolding either way. Slice 1 of Phase 10 widens the adapter to answer with a list while
    /// every caller still takes one, and slice 2b - where the per-folder stores are re-keyed and the
    /// loop over a game's targets arrives - deletes both of these.
    /// </para>
    /// </remarks>
    public ModTarget? SingleTargetOrNone()
    {
        return Count switch
        {
            0 => null,
            1 => _targets[0],
            _ => throw new InvalidOperationException(
                $"This game reaches {Count} mod folders ({string.Join(", ", _targets.Select(x => x.Key))}), " +
                $"and this caller has only been taught about one.")
        };
    }

    /// <summary>
    /// The one target, for a caller that cannot do anything at all without a folder.
    /// </summary>
    /// <remarks>
    /// Reaching no folder is the user's settings rather than a fault, so it is said in a sentence they
    /// can act on. Several is still the tripwire, and still an exception nobody should ever see.
    /// </remarks>
    public ModTarget RequireSingleTarget()
    {
        return SingleTargetOrNone() ?? throw new UserFriendlyException(
            "This game has no mod folder",
            "Its settings point at no folder to put mods in, so there is nothing to sync. Fill one in on the game's settings page.");
    }


    public IEnumerator<ModTarget> GetEnumerator() => ((IEnumerable<ModTarget>)_targets).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _targets.GetEnumerator();
}
