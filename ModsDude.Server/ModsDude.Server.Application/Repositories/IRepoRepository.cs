using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Application.Repositories;
public interface IRepoRepository
{
    Task<bool> CheckNameIsTaken(RepoName name, CancellationToken cancellationToken);
    Task<bool> CheckNameIsTaken(RepoName name, RepoId except, CancellationToken cancellationToken);
    Task<Repo?> GetById(RepoId id);
    void AddNewRepo(Repo repo);
    void Delete(Repo repo);
}
