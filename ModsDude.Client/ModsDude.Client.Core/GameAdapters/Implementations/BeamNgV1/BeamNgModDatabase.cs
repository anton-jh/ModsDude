using ModsDude.Client.Core.Exceptions;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModsDude.Client.Core.GameAdapters.Implementations.BeamNgV1;

/// <summary>
/// The game's <c>mods/db.json</c>: every mod it has found, and whether each one is switched on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only ever switches on, and only the profile's own mods.</b> The game switches every mod off
/// across a major update, and a mod a profile installs but the game leaves off is a mod nobody is
/// playing with. Everything else in the file - other mods, their metadata, the game's own fields -
/// is left exactly as the game wrote it.
/// </para>
/// <para>
/// A mod the game has not listed yet is left alone too. The game lists a new file the next time it
/// starts, switched on.
/// </para>
/// </remarks>
public static class BeamNgModDatabase
{
    public const string FileName = "db.json";

    private static readonly JsonSerializerOptions _writeOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };


    /// <param name="placedFileNames">The file names the profile's mods have directly in the game's <c>mods</c> folder.</param>
    public static GameFileEdit Activate(IEnumerable<string> placedFileNames)
    {
        var paths = placedFileNames
            .Select(x => $"/mods/{x}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new GameFileEdit(FileName, current => Activate(current, paths)) { RecycleReplaced = true };
    }


    /// <param name="fullPaths">The game's own spelling of each file to switch on, <c>/mods/name.zip</c>.</param>
    /// <exception cref="UserFriendlyException">The file is there and is not the JSON object the game writes.</exception>
    internal static byte[]? Activate(byte[]? current, IReadOnlySet<string> fullPaths)
    {
        if (current is null)
        {
            return null;
        }

        var root = Parse(current);

        if (root["mods"] is not JsonObject mods)
        {
            return null;
        }

        var changed = false;

        foreach (var (_, node) in mods)
        {
            if (node is not JsonObject entry ||
                entry["fullpath"] is not JsonValue fullPath ||
                fullPath.TryGetValue<string>(out var path) is false ||
                fullPaths.Contains(path) is false)
            {
                continue;
            }

            if (entry["active"] is JsonValue active && active.TryGetValue<bool>(out var isActive) && isActive)
            {
                continue;
            }

            entry["active"] = true;
            changed = true;
        }

        return changed
            ? JsonSerializer.SerializeToUtf8Bytes(root, _writeOptions)
            : null;
    }


    private static JsonObject Parse(byte[] content)
    {
        try
        {
            using var stream = new MemoryStream(content, writable: false);

            return JsonNode.Parse(stream) as JsonObject
                ?? throw new JsonException("The root is not an object.");
        }
        catch (JsonException exception)
        {
            throw new UserFriendlyException(
                "Could not read the game's mod list",
                "ModsDude reads BeamNG.drive's mods/db.json to switch the profile's mods on, and it is not valid JSON. " +
                "If the game is running, try again once it has closed; otherwise starting the game once rewrites it.",
                exception);
        }
    }
}
