namespace ModsDude.Client.Core.GameProcesses;

/// <summary>Whether any of a game's processes is alive on this machine.</summary>
public interface IGameProcesses
{
    bool IsAnyRunning(IReadOnlyList<string> processNames);
}
