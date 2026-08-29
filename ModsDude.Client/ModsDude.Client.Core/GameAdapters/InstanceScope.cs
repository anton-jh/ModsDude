using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModsDude.Client.Core.GameAdapters;

/// <summary>
/// The identity of the game an adapter is configured for - the adapter id, plus a discriminator its
/// base settings decide where one adapter serves several games. A repo offers the instances whose
/// scope equals its own.
/// </summary>
/// <remarks>
/// A type rather than a bare string because '_farming_simulator#fs25' and '_farming_simulator@1'
/// are both plausible-looking strings, and comparing the wrong pair fails as a silently empty
/// instance list rather than as a compile error.
/// </remarks>
[JsonConverter(typeof(InstanceScopeJsonConverter))]
public readonly record struct InstanceScope
{
    private const string _separator = "#";


    public InstanceScope(string adapterId, string? discriminator = null)
    {
        if (adapterId.Contains(_separator))
        {
            throw new ArgumentException($"Adapter id cannot contain the separator: '{_separator}'");
        }
        if (discriminator?.Contains(_separator) == true)
        {
            throw new ArgumentException($"Discriminator cannot contain the separator: '{_separator}'");
        }

        AdapterId = adapterId;
        Discriminator = string.IsNullOrWhiteSpace(discriminator) ? null : discriminator;
    }


    public string AdapterId { get; }
    public string? Discriminator { get; }


    public override readonly string ToString()
    {
        return Discriminator is null
            ? AdapterId
            : $"{AdapterId}{_separator}{Discriminator}";
    }


    public static InstanceScope Parse(string s)
    {
        return s.Split(_separator) switch
        {
            [var adapterId] => new(adapterId),
            [var adapterId, var discriminator] => new(adapterId, discriminator),
            _ => throw new FormatException($"Invalid InstanceScope string '{s}'")
        };
    }
}

public sealed class InstanceScopeJsonConverter : JsonConverter<InstanceScope>
{
    public override InstanceScope Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return InstanceScope.Parse(reader.GetString()
            ?? throw new JsonException("Expected an instance scope string."));
    }

    public override void Write(Utf8JsonWriter writer, InstanceScope value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
