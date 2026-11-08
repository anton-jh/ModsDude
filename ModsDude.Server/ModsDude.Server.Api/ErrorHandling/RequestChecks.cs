using ModsDude.Server.Domain;
using ModsDude.Server.Domain.Activity;
using ModsDude.Server.Domain.Mods;

namespace ModsDude.Server.Api.ErrorHandling;

/// <summary>
/// The typed refusal for a value a request carries, checked before the domain type is built from it,
/// which would otherwise throw. Each returns null for a value that is fine.
/// </summary>
internal static class RequestChecks
{
    public static CustomProblemDetails? ModId(string? modId)
    {
        return Domain.Mods.ModId.IsValid(modId) ? null : Problems.InvalidModId(modId);
    }

    public static CustomProblemDetails? ModId(string? modId, string? versionId)
    {
        return ModId(modId) ?? (ModVersionId.IsValid(versionId) ? null : Problems.InvalidModVersionId(versionId));
    }

    /// <summary>A version id that may be absent, such as one end of a placement.</summary>
    public static CustomProblemDetails? OptionalVersionId(string? versionId)
    {
        return versionId is null || ModVersionId.IsValid(versionId) ? null : Problems.InvalidModVersionId(versionId);
    }

    public static CustomProblemDetails? Name(string? name)
    {
        return NameRule.IsValid(name) ? null : Problems.InvalidName(name, NameRule.MaximumLength);
    }

    /// <summary>A name that may be absent, where absent means "keep the current one".</summary>
    public static CustomProblemDetails? OptionalName(string? name)
    {
        return string.IsNullOrEmpty(name) ? null : Name(name);
    }

    public static CustomProblemDetails? Label(string? label, int maximumLength)
    {
        return label is { Length: var length } && length > maximumLength ? Problems.InvalidLabel(label, maximumLength) : null;
    }

    public static CustomProblemDetails? FileSize(long sizeBytes)
    {
        return sizeBytes > 0 ? null : Problems.InvalidFileSize(sizeBytes);
    }

    public static CustomProblemDetails? Game(string? game)
    {
        return GameKey.IsValid(game) ? null : Problems.InvalidGameKey(game);
    }
}
