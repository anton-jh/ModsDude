using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Domain.Users;
public class User
{
    private readonly HashSet<RepoMembership> _repoMemberships = [];


    /// <summary>Only <see cref="UserSignUp"/> creates users.</summary>
    internal User(UserId id, DisplayName displayName, DateTime created)
    {
        Id = id;
        DisplayName = displayName;
        Created = created;
        LastSeen = created;
        ProfileLastUpdated = created;
    }


    public UserId Id { get; private set; }

    public DisplayName DisplayName { get; private set; }
    public DateTime Created { get; init; }
    public DateTime LastSeen { get; set; }
    public DateTime ProfileLastUpdated { get; private set; }
    public bool IsTrusted { get; private set; } = false;

    /// <summary>
    /// Counts revocations of trust. A trust code records the epoch it was redeemed in, and one user may
    /// redeem one code per epoch - see <see cref="Invites.TrustCode.RedeemedTrustEpoch"/>.
    /// </summary>
    public int TrustEpoch { get; private set; }

    /// <summary>When the user was blocked from the API, or null while they are not.</summary>
    public DateTime? BlockedAt { get; private set; }

    public bool IsBlocked => BlockedAt is not null;

    /// <summary>
    /// The address of the user's picture in the image store, or null for none - in which case they
    /// are drawn as their initial on their tag's colour.
    /// </summary>
    /// <remarks>
    /// It shares the store mod art lives in rather than having one of its own: it is the same shape
    /// of thing - a small image, immutable at its address, cached forever by every client that draws
    /// it - and storage maintenance counts these addresses as referenced alongside mod versions'.
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

    public void GrantTrust()
    {
        IsTrusted = true;
    }

    public void RevokeTrust()
    {
        if (IsTrusted)
        {
            IsTrusted = false;
            TrustEpoch++;
        }
    }

    /// <summary>Keeps the time of the first block when repeated.</summary>
    public void Block(DateTime now)
    {
        BlockedAt ??= now;
    }

    public void Unblock()
    {
        BlockedAt = null;
    }
}

public readonly record struct UserId(string Value);

/// <summary>
/// What a user is called. Nothing makes it unique and nothing here tries to: two people called Anton
/// are both called Anton, and a list showing both of them disambiguates at the point of display with
/// <see cref="UserTag"/> rather than by editing somebody's name into a shape they never chose.
/// </summary>
public readonly record struct DisplayName(string Value)
{
    /// <summary>Room for a full name, and still short enough to sit beside an avatar in the sidebar.</summary>
    public const int MaximumLength = 32;


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
