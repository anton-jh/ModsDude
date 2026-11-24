using System.Globalization;
using System.Text;

namespace ModsDude.Server.Domain.Storage;

/// <summary>
/// Blob metadata values travel as HTTP headers, which carry ASCII only, so label values are stored
/// percent-encoded as UTF-8. Names in any language survive the round trip.
/// </summary>
/// <remarks>
/// Storage caps all of a blob's metadata at 8 KiB together, so each encoded value is capped at
/// <see cref="MaximumEncodedLength"/>: every label key at its longest still fits next to the content hash.
/// A longer value is cut short at a whole character and ends in an ellipsis.
/// </remarks>
public static class BlobMetadataText
{
    public const int MaximumEncodedLength = 512;

    private const string _ellipsis = "…";


    public static string Encode(string value)
    {
        var encoded = Uri.EscapeDataString(value);

        if (encoded.Length <= MaximumEncodedLength)
        {
            return encoded;
        }

        var budget = MaximumEncodedLength - Uri.EscapeDataString(_ellipsis).Length;
        var kept = new StringBuilder();
        var used = 0;
        var elements = StringInfo.GetTextElementEnumerator(value);

        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            var length = Uri.EscapeDataString(element).Length;

            if (used + length > budget)
            {
                break;
            }

            kept.Append(element);
            used += length;
        }

        return Uri.EscapeDataString(kept.Append(_ellipsis).ToString());
    }

    /// <summary>The value decoded. Anything that is not a valid escape is left as stored.</summary>
    public static string Decode(string value) => Uri.UnescapeDataString(value);
}
