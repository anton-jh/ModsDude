using ModsDude.Server.Domain.Exceptions;

namespace ModsDude.Server.Domain;

/// <summary>
/// What a repo, a profile or a savegame may be called. They are read by people picking one from a
/// list, so the things worth refusing are an empty name, an essay, and characters nobody can see or
/// that a game's own save files cannot hold: control characters and broken surrogate pairs. Stored
/// trimmed.
/// </summary>
public static class NameRule
{
    public const int MaximumLength = 100;


    public static bool IsValid(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Trim().Length <= MaximumLength
            && HasOnlyPrintableCharacters(value.Trim());
    }

    public static string Validated(string value, string what)
    {
        return IsValid(value)
            ? value.Trim()
            : throw new DomainValidationException($"'{value}' is not a valid {what} name. It must not be empty, longer than {MaximumLength} characters, or hold control characters.");
    }


    private static bool HasOnlyPrintableCharacters(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsControl(value[i]))
            {
                return false;
            }

            if (char.IsSurrogate(value[i]))
            {
                if (i + 1 >= value.Length || char.IsSurrogatePair(value[i], value[i + 1]) is false)
                {
                    return false;
                }

                i++;
            }
        }

        return true;
    }
}
