namespace ModsDude.Client.Core.GameAdapters;

public interface IGameAdapterIndex
{
    IEnumerable<IGameAdapter> GetAllLatest();
    IGameAdapter? GetById(GameAdapterId id);
    IGameAdapter? GetLatestByPartialId(string id);
}

internal class GameAdapterIndex : IGameAdapterIndex
{
    private readonly IEnumerable<IGameAdapter> _allGameAdapters;


    public GameAdapterIndex(IEnumerable<IGameAdapter> allGameAdapters)
    {
        var duplicates = allGameAdapters
            .GroupBy(x => x.Descriptor.Id)
            .Where(x => x.Count() > 1);

        if (duplicates.Any())
        {
            throw new ArgumentException($"Found duplicated game adapters: {string.Join(',', duplicates.Select(x => x.Key))}");
        }

        _allGameAdapters = allGameAdapters;
    }


    public IGameAdapter? GetById(GameAdapterId id)
    {
        return _allGameAdapters.FirstOrDefault(x => x.Descriptor.Id == id);
    }

    public IGameAdapter? GetLatestByPartialId(string id)
    {
        return _allGameAdapters
            .Where(x => x.Descriptor.Id.Id == id)
            .MaxBy(x => x.Descriptor.Id.CompatibilityVersion);
    }

    public IEnumerable<IGameAdapter> GetAllLatest()
    {
        return _allGameAdapters
            .GroupBy(x => x.Descriptor.Id.Id)
            .Select(x => x.OrderByDescending(x => x.Descriptor.Id.CompatibilityVersion).First());
    }
}
