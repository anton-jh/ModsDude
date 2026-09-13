namespace ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;

/// <summary>
/// Farming Simulator reaches one folder pair, and this is its key.
/// </summary>
/// <remarks>
/// <para>
/// <b>One key, two capability adapters.</b> The mod half files its manifest under it and the
/// savegame half files its bindings under it, and they mean the same target: the saves in this
/// game's data folder were played against the mods in the <c>mods</c> folder beside them. Two
/// constants would be two things to keep in step, and a machine whose halves disagreed would
/// attribute nobody's evening to anything.
/// </para>
/// <para>
/// <b>Stable, and it has to stay that way.</b> Renaming this orphans every manifest and every
/// savegame binding on every member's machine, and nothing can tell that from the folders having been
/// taken away. It was spelled <c>mods</c> until it was renamed once, before there were any users to
/// orphan: a target is the mod folder <em>and</em> the savegame folder beside it, so a key naming
/// only the first read as a mistake everywhere a slot was addressed.
/// </para>
/// </remarks>
public static class FarmingSimulatorTarget
{
    public static TargetKey Key { get; } = new("game");
}
