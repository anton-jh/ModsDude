using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModsDude.Client.Core.GameAdapters;

/// <summary>
/// The identity of the game an adapter is configured for - the adapter id, plus a discriminator its
/// base settings decide where one adapter serves several games. A repo offers the games whose
/// scope equals its own.
/// </summary>
/// <remarks>
/// A type rather than a bare string because '_farming_simulator#fs25' and '_farming_simulator@1'
/// are both plausible-looking strings, and comparing the wrong pair fails as a silently empty
/// game list rather than as a compile error.
/// </remarks>
[JsonConverter(typeof(GameIdentityJsonConverter))]
public readonly record struct GameIdentity
{
    private const string _separator = "#";


    public GameIdentity(string adapterId, string? discriminator = null)
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


    public static GameIdentity Parse(string s)
    {
        return s.Split(_separator) switch
        {
            [var adapterId] => new(adapterId),
            [var adapterId, var discriminator] => new(adapterId, discriminator),
            _ => throw new FormatException($"Invalid GameIdentity string '{s}'")
        };
    }
}

/// <summary>
/// A game identity as a string, both as a value and as a property name.
/// </summary>
/// <remarks>
/// The property-name half is what lets <see cref="Persistence.LocalState.Games"/> be keyed by one.
/// Without it a record struct key silently serializes as an object - the default implementations of
/// these two throw, and <c>Dictionary&lt;GameIdentity, …&gt;</c> is exactly where that is reached.
/// </remarks>
public sealed class GameIdentityJsonConverter : JsonConverter<GameIdentity>
{
    public override GameIdentity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return GameIdentity.Parse(reader.GetString()
            ?? throw new JsonException("Expected a game identity string."));
    }

    public override void Write(Utf8JsonWriter writer, GameIdentity value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }

    public override GameIdentity ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return GameIdentity.Parse(reader.GetString()
            ?? throw new JsonException("Expected a game identity property name."));
    }

    public override void WriteAsPropertyName(Utf8JsonWriter writer, GameIdentity value, JsonSerializerOptions options)
    {
        writer.WritePropertyName(value.ToString());
    }
}
