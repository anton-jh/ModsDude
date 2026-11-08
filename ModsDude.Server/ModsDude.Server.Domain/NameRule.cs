using ModsDude.Server.Domain.Exceptions;

namespace ModsDude.Server.Domain;

/// <summary>
/// What a repo, a profile or a savegame may be called. They are read by people picking one from a
/// list, so the only things worth refusing are an empty name and an essay. Stored trimmed.
/// </summary>
public static class NameRule
{
    public const int MaximumLength = 100;


    public static bool IsValid(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= MaximumLength;
    }

    public static string Validated(string value, string what)
    {
        return IsValid(value)
            ? value.Trim()
            : throw new DomainValidationException($"'{value}' is not a valid {what} name. It must not be empty or longer than {MaximumLength} characters.");
    }
}
