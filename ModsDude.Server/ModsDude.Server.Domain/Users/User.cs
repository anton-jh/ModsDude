using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Domain.Users;
public class User(UserId id, DisplayName displayName, DateTime created)
{
    private readonly HashSet<RepoMembership> _repoMemberships = [];


    public UserId Id { get; private set; } = id;

    public DisplayName DisplayName { get; set; } = displayName;
    public DateTime Created { get; init; } = created;
    public DateTime LastSeen { get; set; } = created;
    public DateTime ProfileLastUpdated { get; set; } = created;
    public bool IsTrusted { get; private set; } = false;

    public IEnumerable<RepoMembership> RepoMemberships => _repoMemberships;
}

public readonly record struct UserId(string Value);

/// <summary>
/// What a user is called, exactly as their identity provider says it. Nothing makes it unique and
/// nothing here tries to: two people called Anton are both called Anton, and a list showing both of
/// them disambiguates at the point of display with <see cref="UserTag"/> rather than by editing
/// somebody's name into a shape they never chose.
/// </summary>
public readonly record struct DisplayName(string Value)
{
    /// <summary>
    /// What a user with no usable name claim is called. A claim can be absent or blank, and that
    /// must not be the difference between being able to use the app and not.
    /// </summary>
    public const string Fallback = "Unnamed user";


    public static DisplayName FromClaim(string? claimValue)
    {
        return new(string.IsNullOrWhiteSpace(claimValue)
            ? Fallback
            : claimValue.Trim());
    }
}
