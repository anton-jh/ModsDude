using System.Diagnostics;

namespace ModsDude.Client.Core.GameProcesses;

/// <summary><see cref="IGameProcesses"/> over the machine's own process list.</summary>
public sealed class SystemGameProcesses : IGameProcesses
{
    public bool IsAnyRunning(IReadOnlyList<string> processNames)
    {
        foreach (var name in processNames)
        {
            var found = Process.GetProcessesByName(name);

            foreach (var process in found)
            {
                process.Dispose();
            }

            if (found.Length > 0)
            {
                return true;
            }
        }

        return false;
    }
}
