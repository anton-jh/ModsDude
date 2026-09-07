namespace ModsDude.Client.Core.Repos;

/// <summary>
/// How a repo is drawn: its own name, and a tag shown only where the list it is in actually needs
/// one.
/// </summary>
/// <remarks>
/// The same rule as <see cref="Users.UserDisplay"/>, for the same reason. Ambiguity is a property of
/// the list rather than of the repo, so it is decided here at the moment of rendering rather than
/// baked into anybody's name: a sidebar where no two repos share a name shows no tags at all, and
/// one where two Vanillas meet shows the tag on <i>both</i> of them, because neither of them is the
/// Vanilla and the other one the duplicate.
/// </remarks>
public static class RepoDisplay
{
    /// <summary>
    /// The ids of the repos in <paramref name="repos"/> that share their name with another repo in
    /// the same set - the only ones whose tag is worth the space.
    /// </summary>
    public static IReadOnlySet<Guid> FindAmbiguous(IEnumerable<(Guid Id, string Name)> repos)
    {
        return repos
            .GroupBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Where(x => x.Count() > 1)
            .SelectMany(x => x)
            .Select(x => x.Id)
            .ToHashSet();
    }
}
