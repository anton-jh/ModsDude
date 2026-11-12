using ModsDude.Client.Core.Exceptions;

namespace ModsDude.Client.Core.GameProcesses;

public sealed class GameRunningException : UserFriendlyException
{
    public GameRunningException(string gameName)
        : this(gameName, $"{gameName} is running. Close it and try again.")
    {
    }

    private GameRunningException(string gameName, string userMessage)
        : base(userMessage)
    {
        GameName = gameName;
    }


    public string GameName { get; }


    /// <summary>The game's processes are unknown, so whether it is running cannot be told.</summary>
    public static GameRunningException Unknown(string gameName)
        => new(gameName, $"Can't tell whether {gameName} is running, so nothing was changed.");
}
