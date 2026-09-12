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
/// <b>Stable, and it has to stay that way.</b> Renaming this would orphan every manifest and every
/// savegame binding on every member's machine, and nothing could tell that from the folders having
/// been taken away. It spells <c>mods</c> because that is what it was named when this game had
/// nothing but a mod folder, and a key is not a description.
/// </para>
/// </remarks>
public static class FarmingSimulatorTarget
{
    public static TargetKey Key { get; } = new("mods");
}
