using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.GameAdapters.Implementations.BeamNgV1;

/// <summary>
/// What a BeamNG.drive mod is tagged with: the kinds of content it adds, read off the folders it
/// holds - the same folders the game loads each kind from.
/// </summary>
internal static class BeamNgModAttributes
{
    public const string TypeKey = "type";

    public const string Vehicle = "vehicle";
    public const string Map = "map";
    public const string Ui = "ui";
    public const string Script = "script";

    /// <summary>Each content folder and the type it makes the mod, in the order types are reported.</summary>
    private static readonly (string Folder, string Type)[] _folderTypes =
    [
        ("vehicles", Vehicle),
        ("levels", Map),
        ("ui", Ui),
        ("lua", Script),
        ("scripts", Script)
    ];


    public static IReadOnlyList<ModAttributeDefinition> Definitions { get; } =
    [
        new(TypeKey, [], [Vehicle, Map, Ui, Script])
    ];

    /// <param name="folders">The archive's top-level folders.</param>
    public static IReadOnlyList<ModAttribute> Read(IReadOnlySet<string> folders)
    {
        return [.. _folderTypes
            .Where(x => folders.Contains(x.Folder))
            .Select(x => x.Type)
            .Distinct()
            .Select(x => new ModAttribute(TypeKey, x))];
    }
}
