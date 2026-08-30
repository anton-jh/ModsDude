namespace ModsDude.Server.Domain.Users;

/// <summary>
/// Turns the identity provider's display name into the unique <see cref="Username"/> this system
/// stores.
/// </summary>
/// <remarks>
/// <para>
/// The name claim is a display name and nothing makes it unique, while <c>Username</c> is unique by
/// index — it is what one member types to add another to a repo, so it has to identify exactly one
/// person. Users are provisioned automatically on their first authenticated request, so there is no
/// form on which a collision could be reported and no second chance to ask: the collision has to be
/// resolved without the user, and the resolution has to be one they can recognise as themselves.
/// </para>
/// <para>
/// Hence a numeric suffix on the name they already answer to, rather than a hash of their subject id
/// or their email address. It is deterministic — the same desired name against the same set of taken
/// names always yields the same answer — and it cannot reach another user's row, because the answer
/// is by construction a name nobody holds and the identity of a user is their subject id in any case.
/// </para>
/// </remarks>
public static class UsernameAllocator
{
    /// <summary>
    /// What a user with no usable name claim is called before disambiguation. A claim can be absent
    /// or blank, and that must not be the difference between being able to use the app and not.
    /// </summary>
    public const string FallbackDisplayName = "Unnamed user";

    /// <summary>
    /// A bound rather than a real limit: reaching it means a thousand people share one display name,
    /// at which point refusing is more honest than continuing to count.
    /// </summary>
    public const int MaximumCandidates = 1000;


    public static Username FromDisplayName(string? displayName)
    {
        return new Username(string.IsNullOrWhiteSpace(displayName)
            ? FallbackDisplayName
            : displayName.Trim());
    }

    /// <summary>
    /// The names <paramref name="desired"/> may resolve to, in the order they should be tried: the
    /// name itself first, so that the overwhelmingly common no-collision case stores exactly what the
    /// provider said.
    /// </summary>
    public static IEnumerable<Username> GetCandidates(Username desired)
    {
        yield return desired;

        for (var suffix = 2; suffix <= MaximumCandidates; suffix++)
        {
            yield return new Username($"{desired.Value} ({suffix})");
        }
    }
}
