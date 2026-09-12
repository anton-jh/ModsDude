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
}
