using ModsDude.Client.Core.Helpers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModsDude.Client.Core.Models;

/// <summary>
/// Somewhere to look for mods to import. Not a sync target: sync makes an instance's mod folder
/// match a profile, which means uninstalling from it, and nothing will ever delete, move or
/// quarantine a file in Downloads or a folder the user pointed at.
/// See docs/09-mod-catalog.md#sources-are-not-sync-targets.
/// </summary>
public record ModSource(ModSourceId Id, string Name, string Path, ModSourceKind Kind);

public enum ModSourceKind
{
    /// <summary>An instance's mod folder. Present automatically, and disabling it here does not affect syncing to it.</summary>
    Instance,

    /// <summary>The system Downloads folder. Once per machine, not per instance.</summary>
    Downloads,

    /// <summary>A folder the user added for this session. Never persisted.</summary>
    AdHoc
}

/// <summary>
/// Identifies a source across sessions, which is what lets "do not look in this folder" be
/// remembered. Ad-hoc sources get one too, so the same code can enable and disable every kind, but
/// theirs is never written to disk.
/// </summary>
[JsonConverter(typeof(ModSourceIdJsonConverter))]
public readonly record struct ModSourceId
{
    private readonly string? _value;


    private ModSourceId(string value)
    {
        _value = value;
    }


    public string Value => _value ?? string.Empty;


    /// <summary>The one source that exists once per machine, so it needs no discriminator.</summary>
    public static ModSourceId Downloads { get; } = new("downloads");

    public static ModSourceId ForInstance(Guid instanceId) => new($"instance:{instanceId}");

    /// <summary>
    /// Keyed by the folder itself, so the same folder added twice is the same source - and so a
    /// disabled folder stays disabled however the user reaches it.
    /// </summary>
    public static ModSourceId ForFolder(string path) => new($"folder:{FileSystemHelper.NormalizePathForComparison(path)}");

    public static ModSourceId Parse(string s) => string.IsNullOrWhiteSpace(s)
        ? throw new FormatException("A mod source id cannot be empty.")
        : new(s);

    public override string ToString() => Value;
}

public sealed class ModSourceIdJsonConverter : JsonConverter<ModSourceId>
{
    public override ModSourceId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return ModSourceId.Parse(reader.GetString()
            ?? throw new JsonException("Expected a mod source id string."));
    }

    public override void Write(Utf8JsonWriter writer, ModSourceId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
