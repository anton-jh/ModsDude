using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Text.Json;

namespace ModsDude.Server.Persistence.ValueConverters;

/// <summary>A blob's metadata as a <c>jsonb</c> object, as storage reported it.</summary>
internal static class BlobMetadataConversion
{
    public static PropertyBuilder<IReadOnlyDictionary<string, string>> AsBlobMetadata(this PropertyBuilder<IReadOnlyDictionary<string, string>> property)
    {
        return property
            .HasColumnType("jsonb")
            .HasConversion(new Converter(), new Comparer());
    }


    private sealed class Converter()
        : ValueConverter<IReadOnlyDictionary<string, string>, string>(
            model => JsonSerializer.Serialize(model, (JsonSerializerOptions?)null),
            provider => Read(provider));

    private sealed class Comparer()
        : ValueComparer<IReadOnlyDictionary<string, string>>(
            (left, right) => left != null && right != null && left.Count == right.Count && !left.Except(right).Any(),
            model => model.Aggregate(0, (hash, entry) => hash ^ HashCode.Combine(entry.Key, entry.Value)),
            model => new Dictionary<string, string>(model, StringComparer.OrdinalIgnoreCase));


    private static IReadOnlyDictionary<string, string> Read(string json)
    {
        var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];

        return new Dictionary<string, string>(entries, StringComparer.OrdinalIgnoreCase);
    }
}
