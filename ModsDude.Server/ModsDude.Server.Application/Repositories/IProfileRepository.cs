using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Application.Repositories;
public interface IProfileRepository
{
    Task<bool> CheckNameIsTaken(RepoId repoId, ProfileName name, CancellationToken cancellationToken);
    Task<bool> CheckNameIsTaken(RepoId repoId, ProfileName name, ProfileId except, CancellationToken cancellationToken);
    Task<Profile?> GetById(RepoId repoId, ProfileId id, CancellationToken cancellationToken);
    void AddNewProfile(Profile profile);
    void Delete(Profile profile);
}
