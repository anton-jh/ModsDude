using ModsDude.Server.Domain.Tags;

namespace ModsDude.Server.Domain.Users;

/// <summary>
/// Four digits that separate two users sharing a <see cref="DisplayName"/>.
/// </summary>
/// <remarks>
/// Derived from the subject id - see <see cref="FourDigitTag"/> for what that buys and what a tag
/// is not. Repos are told apart the same way, by <see cref="Repos.RepoTag"/>.
/// </remarks>
public static class UserTag
{
    public static string For(UserId userId)
    {
        return FourDigitTag.For(userId.Value);
    }
}
