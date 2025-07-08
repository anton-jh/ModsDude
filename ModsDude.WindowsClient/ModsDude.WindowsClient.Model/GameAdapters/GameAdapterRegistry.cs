using ModsDude.WindowsClient.Model.Exceptions;

namespace ModsDude.WindowsClient.Model.GameAdapters;
public class GameAdapterRegistry(IEnumerable<IGameAdapter> gameAdapters)
{
    private readonly Dictionary<GameAdapterId, IGameAdapter> _byId =
        gameAdapters.ToDictionary(x => x.Descriptor.Id);


    public IEnumerable<GameAdapterDescriptor> Descriptors { get; } =
        gameAdapters.Select(x => x.Descriptor);


    public IGameAdapter Get(GameAdapterId id)
    {
        if (!_byId.TryGetValue(id, out var gameAdapter))
        {
            throw new UserFriendlyException($"Unsupported game adapter '{id}'.");
        }

        return gameAdapter;
    }
}
