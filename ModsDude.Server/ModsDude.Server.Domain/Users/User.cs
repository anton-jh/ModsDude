using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Domain.Users;
public class User(UserId id, DisplayName displayName, DateTime created)
{
    private readonly HashSet<RepoMembership> _repoMemberships = [];


    public UserId Id { get; private set; } = id;

    public DisplayName DisplayName { get; private set; } = displayName;
    public DateTime Created { get; init; } = created;
    public DateTime LastSeen { get; set; } = created;
    public DateTime ProfileLastUpdated { get; private set; } = created;
    public bool IsTrusted { get; private set; } = false;

    /// <summary>
    /// The address of the user's picture in the image store, or null for none - in which case they
    /// are drawn as their initial on their tag's colour.
    /// </summary>
    /// <remarks>
    /// It shares the store mod art lives in rather than having one of its own: it is the same shape
    /// of thing - a small image, immutable at its address, cached forever by every client that draws
    /// it - and the reclamation sweep counts these addresses as referenced alongside mod versions'.
    /// </remarks>
    public string? AvatarHash { get; private set; }

    public IEnumerable<RepoMembership> RepoMemberships => _repoMemberships;


    public void Rename(DisplayName displayName, DateTime now)
    {
        DisplayName = displayName;
        ProfileLastUpdated = now;
    }

    /// <param name="avatarHash">An image address the caller has checked is stored, or null to remove the picture.</param>
    public void SetAvatar(string? avatarHash, DateTime now)
    {
        AvatarHash = avatarHash;
        ProfileLastUpdated = now;
    }

    internal void GrantTrust()
    {
        IsTrusted = true;
    }
}

public readonly record struct UserId(string Value);

/// <summary>
/// What a user is called. Nothing makes it unique and nothing here tries to: two people called Anton
/// are both called Anton, and a list showing both of them disambiguates at the point of display with
/// <see cref="UserTag"/> rather than by editing somebody's name into a shape they never chose.
/// </summary>
/// <remarks>
/// Seeded from the identity provider's <c>name</c> claim the first time the user is seen, and theirs
/// to change from then on. The claim is never read again: the provider has no page for editing it,
/// so a name that kept following the claim would be a name nobody could change.
/// </remarks>
public readonly record struct DisplayName(string Value)
{
    /// <summary>
    /// What a user with no usable name claim is called. A claim can be absent or blank, and that
    /// must not be the difference between being able to use the app and not.
    /// </summary>
    public const string Fallback = "Unnamed user";

    /// <summary>Room for a full name, and still short enough to sit beside an avatar in the sidebar.</summary>
    public const int MaximumLength = 32;


    /// <summary>
    /// The provider's name as a starting point. Never refused, only made to fit: a claim is not
    /// something the user typed, so there is nobody to send it back to.
    /// </summary>
    public static DisplayName FromClaim(string? claimValue)
    {
        var cleaned = new string([.. (claimValue ?? "").Where(x => !char.IsControl(x))]).Trim();

        if (cleaned.Length > MaximumLength)
        {
            cleaned = cleaned[..MaximumLength].TrimEnd();
        }

        return new(cleaned.Length == 0 ? Fallback : cleaned);
    }

    /// <summary>A name the user typed, trimmed, or the reason it cannot be one.</summary>
    public static bool TryParse(string? input, out DisplayName displayName, out string error)
    {
        displayName = default;
        var trimmed = input?.Trim() ?? "";

        if (trimmed.Length == 0)
        {
            error = "A name cannot be empty.";
            return false;
        }

        if (trimmed.Length > MaximumLength)
        {
            error = $"A name cannot be longer than {MaximumLength} characters.";
            return false;
        }

        if (trimmed.Any(char.IsControl))
        {
            error = "A name cannot contain control characters.";
            return false;
        }

        displayName = new(trimmed);
        error = "";
        return true;
    }
}

/// <summary>
/// What a list needs to draw somebody it only has the id of: their name and their picture.
/// </summary>
public readonly record struct UserNameplate(DisplayName DisplayName, string? AvatarHash)
{
    public static UserNameplate Of(User user) => new(user.DisplayName, user.AvatarHash);
}
