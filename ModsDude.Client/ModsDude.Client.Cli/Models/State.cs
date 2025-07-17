using ModsDude.Client.Core.Persistence;

namespace ModsDude.Client.Cli.Models;
internal class State(int version)
    : IState<State>
{
    public int Version { get; set; } = version;

    public static State Create(int version)
    {
        return new(version);
    }
}
