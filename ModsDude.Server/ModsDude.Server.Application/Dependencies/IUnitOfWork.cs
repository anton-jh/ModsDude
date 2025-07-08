namespace ModsDude.Server.Application.Dependencies;
public interface IUnitOfWork
{
    public Task CommitAsync(CancellationToken cancellationToken);
}
