namespace ModsDude.Client.Core.Services;

public interface ILastSelectionRepository
{
    Guid? GetLastRepo(IEnumerable<Guid> offered);

    Guid? GetLastProfile(IEnumerable<Guid> offered);

    void RecordRepo(Guid repoId);

    void RecordProfile(Guid profileId);
}
