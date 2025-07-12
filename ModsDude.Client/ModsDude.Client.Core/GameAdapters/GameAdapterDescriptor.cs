namespace ModsDude.Client.Core.GameAdapters;
public record struct GameAdapterDescriptor(
    GameAdapterId Id,
    string DisplayName,
    IEnumerable<string> CompatibleWithGames,
    string Description
);
