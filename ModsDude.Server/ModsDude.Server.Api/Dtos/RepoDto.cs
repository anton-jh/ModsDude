using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Api.Dtos;

public record RepoDto(Guid Id, string Name, string AdapterId, string AdapterConfiguration)
{
    public static RepoDto FromModel(Repo repo)
    {
        return new(
            repo.Id.Value,
            repo.Name.Value,
            repo.AdapterData.Id.Value,
            repo.AdapterData.Configuration.Value);
    }
}
