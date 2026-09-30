using ModsDude.Client.Core.Exceptions;

namespace ModsDude.Client.Core.GameProcesses;

public sealed class GameRunningException(string gameName)
    : UserFriendlyException($"{gameName} is running. Close it and try again.")
{
    public string GameName { get; } = gameName;
}
