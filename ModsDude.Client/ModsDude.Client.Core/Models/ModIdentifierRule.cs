namespace ModsDude.Client.Core.Models;

/// <summary>
/// What the server accepts as a mod id or a mod version: both become segments of a blob name, so
/// neither may hold a separator, end in a dot or carry control characters. The server checks the
/// same rule; checking it here keeps a mod the server would refuse out of the catalog.
/// </summary>
internal static class ModIdentifierRule
{
    public const int MaximumLength = 128;


    /// <param name="normalized">The value after trimming, which is how both keys store it.</param>
    public static bool IsValid(string normalized)
    {
        return normalized.Length is > 0 and <= MaximumLength
            && !normalized.EndsWith('.')
            && !normalized.Any(x => x is '/' or '\\' || char.IsControl(x));
    }
}
