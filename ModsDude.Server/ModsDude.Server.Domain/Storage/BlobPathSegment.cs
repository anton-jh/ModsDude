using ModsDude.Server.Domain.Exceptions;

namespace ModsDude.Server.Domain.Storage;

/// <summary>
/// One segment of a blob name built from a client-chosen value, such as a mod id or a version id.
/// </summary>
/// <remarks>
/// <para>
/// The rule is what keeps a segment one segment. A separator would add a level, and <c>.</c> or
/// <c>..</c> would let <see cref="Uri"/> collapse the path into another repo's blobs. A trailing dot
/// is dropped from path segments by <see cref="Uri"/> as well, so the blob written would not be the
/// one named.
/// </para>
/// <para>
/// Surrounding whitespace and control characters are refused because nothing writes them on purpose,
/// and a name that differs from another only by them cannot be told apart in a list.
/// </para>
/// </remarks>
public static class BlobPathSegment
{
    public const int MaximumLength = 128;


    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumLength)
        {
            return false;
        }

        if (value != value.Trim() || value.EndsWith('.'))
        {
            return false;
        }

        return !value.Any(x => x is '/' or '\\' || char.IsControl(x));
    }

    public static string Validated(string value)
    {
        return IsValid(value)
            ? value
            : throw new DomainValidationException($"'{value}' is not a valid blob path segment");
    }
}
