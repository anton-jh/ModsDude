using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Storage;

namespace ModsDude.Server.Domain.Mods;

/// <summary>
/// A mod's identity within its repo: its file name's stem, lower-cased by the client. Also a blob path
/// segment, so it follows <see cref="BlobPathSegment"/>.
/// </summary>
public readonly record struct ModId
{
    public ModId(string value)
    {
        if (!IsValid(value))
        {
            throw new DomainValidationException($"'{value}' is not a valid mod id");
        }

        Value = value;
    }


    public string Value { get; }


    public static bool IsValid(string? value) => BlobPathSegment.IsValid(value);
}
