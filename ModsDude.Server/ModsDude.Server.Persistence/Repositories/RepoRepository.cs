using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Application.Repositories;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Persistence.Repositories;
public class RepoRepository(ApplicationDbContext dbContext) : IRepoRepository
{
    public Task<bool> CheckNameIsTaken(RepoName name, CancellationToken cancellationToken)
    {
        return dbContext.Repos.AnyAsync(x => x.Name == name, cancellationToken);
    }

    public Task<bool> CheckNameIsTaken(RepoName name, RepoId except, CancellationToken cancellationToken)
    {
        return dbContext.Repos.AnyAsync(x => x.Name == name && x.Id != except, cancellationToken);
    }

    public void AddNewRepo(Repo repo)
    {
        dbContext.Repos.Add(repo);
    }

    public async Task<Repo?> GetById(RepoId repoId)
    {
        return await dbContext.Repos.FindAsync(repoId);
    }

    public void Delete(Repo repo)
    {
        dbContext.Repos.Remove(repo);
    }
}
