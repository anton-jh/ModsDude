using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Api.Dtos;

/// <param name="ArchivedAt">When it was put away, or null while it is live.</param>
public record RepoDto(Guid Id, string Name, string AdapterId, string AdapterConfiguration, DateTime? ArchivedAt)
{
    public static RepoDto FromModel(Repo repo)
    {
        return new(
            repo.Id.Value,
            repo.Name.Value,
            repo.AdapterData.Id.Value,
            repo.AdapterData.Configuration.Value,
            repo.ArchivedAt);
    }
}
