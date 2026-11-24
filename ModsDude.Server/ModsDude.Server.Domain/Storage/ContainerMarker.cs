using System.Text.Json;

namespace ModsDude.Server.Domain.Storage;

/// <summary>
/// What the marker blob in each container says: the database the container belongs to, the generation
/// that database was at when it last advanced, and which advance wrote it. See <see cref="StorageBinding"/>.
/// </summary>
/// <param name="Generation">Null for a marker that names only a database, which predates generations.</param>
/// <param name="AdvanceId">The advance or rebind that wrote the marker. Null until the binding first advances.</param>
public sealed record ContainerMarker(Guid BindingId, long? Generation, Guid? AdvanceId = null)
{
    public string Format()
    {
        return JsonSerializer.Serialize(new MarkerContent(
            BindingId,
            Generation ?? throw new InvalidOperationException("A marker is always written with a generation."),
            AdvanceId));
    }

    public static ContainerMarker Parse(string text)
    {
        if (Guid.TryParse(text, out var legacy))
        {
            return new ContainerMarker(legacy, null);
        }

        MarkerContent? content;

        try
        {
            content = JsonSerializer.Deserialize<MarkerContent>(text);
        }
        catch (JsonException exception)
        {
            throw new FormatException(_unreadable, exception);
        }

        return content is not null && content.BindingId != Guid.Empty && content.Generation >= 0 && content.AdvanceId != Guid.Empty
            ? new ContainerMarker(content.BindingId, content.Generation, content.AdvanceId)
            : throw new FormatException(_unreadable);
    }


    private const string _unreadable = "The storage binding marker does not hold a binding id and a generation.";


    private sealed record MarkerContent(Guid BindingId, long Generation, Guid? AdvanceId);
}
