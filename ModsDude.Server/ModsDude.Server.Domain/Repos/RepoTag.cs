using ModsDude.Server.Domain.Tags;
using System.Globalization;

namespace ModsDude.Server.Domain.Repos;

/// <summary>
/// Four digits that separate two repos sharing a <see cref="RepoName"/>.
/// </summary>
/// <remarks>
/// <para>
/// Repo names are not unique - not globally, and not among the repos any one person is in. Nothing
/// looks a repo up by name (the only way into one is an invite code), so uniqueness bought nothing
/// but a promise that two sidebar entries would never read the same, and it charged for that
/// promise with a rename forced on whoever named their repo second.
/// </para>
/// <para>
/// This is what pays for it instead, and only where it has to: a client shows the tag on the repos
/// in a list that actually holds two of a name. Derived from the id - see <see cref="FourDigitTag"/>
/// - so it is the same four digits for every member and it survives a rename.
/// </para>
/// </remarks>
public static class RepoTag
{
    public static string For(RepoId repoId)
    {
        return FourDigitTag.For(repoId.Value.ToString("D", CultureInfo.InvariantCulture));
    }
}
