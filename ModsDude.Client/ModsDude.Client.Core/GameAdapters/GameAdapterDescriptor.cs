namespace ModsDude.Client.Core.GameAdapters;
public record struct GameAdapterDescriptor(
    GameAdapterId Id,
    string DisplayName,
    string Description
);
