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
}
