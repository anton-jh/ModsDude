using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Api.Dtos;

/// <summary>
/// <paramref name="Name"/> is what the repo is called and is not unique - not on the server, and not
/// among the repos one person is in; <paramref name="Tag"/> is four digits that separate it from any
/// other repo called the same. A client shows the tag only where a list actually holds two of a name
/// - see <see cref="RepoTag"/> for why it is not a suffix on the name itself.
/// </summary>
/// <param name="ArchivedAt">When it was put away, or null while it is live.</param>
public record RepoDto(Guid Id, string Name, string Tag, string AdapterId, string AdapterConfiguration, DateTime? ArchivedAt)
{
    public static RepoDto FromModel(Repo repo)
    {
        return new(
            repo.Id.Value,
            repo.Name.Value,
            RepoTag.For(repo.Id),
            repo.AdapterData.Id.Value,
            repo.AdapterData.Configuration.Value,
            repo.ArchivedAt);
    }
}
