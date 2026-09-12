using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Tests;

/// <summary>
/// The tests are written in plain strings because that is what a filename and a modDesc carry. This
/// is the one place they cross into the key types, which is also where the normalization under test
/// happens in the app.
/// </summary>
internal static class Keys
{
    public static ModKey Mod(string id) => ModKey.From(id);

    public static ModVersionKey V(string version) => ModVersionKey.From(version);

    public static ModVersionKey[] Vs(params string[] versions) => [.. versions.Select(ModVersionKey.From)];

    /// <summary>
    /// The game a test keys its state on. Any identity would do; this is the one the Farming
    /// Simulator adapter really answers with, so a manifest filename in a test temp folder looks like
    /// the one on a real machine.
    /// </summary>
    public static GameIdentity Game(string discriminator = "fs25") => new("farmingSimulator", discriminator);

    /// <summary>
    /// The target a test files a manifest under. The default key is the one Farming Simulator's own
    /// adapter names its single folder, so a manifest in a test temp folder is named as it would be
    /// on a real machine; a second key is how a test reaches the multi-target half.
    /// </summary>
    public static ModTargetRef Target(string key = "mods", string discriminator = "fs25")
        => new(Game(discriminator), new TargetKey(key));

    /// <summary>
    /// A place a savegame can be, in the same default target a manifest is filed under - so a test
    /// that says nothing about targets is a one-folder game, which is what nearly every machine is.
    /// Naming a second key is how a test reaches the two-folder half.
    /// </summary>
    public static SavegameSlotRef Slot(string slot, string target = "mods")
        => new(new TargetKey(target), new SavegameSlotId(slot));
}
