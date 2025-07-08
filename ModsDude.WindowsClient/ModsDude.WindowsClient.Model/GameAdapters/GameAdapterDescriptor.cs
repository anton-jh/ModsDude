namespace ModsDude.WindowsClient.Model.GameAdapters;
public record struct GameAdapterDescriptor(
    GameAdapterId Id,
    string DisplayName,
    IEnumerable<string> CompatibleWithGames,
    string Description
);
