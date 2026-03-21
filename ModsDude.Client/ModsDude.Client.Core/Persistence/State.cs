using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Persistence;

public class State
{
    public int Version { get; set; } = 1;
    public List<Guid> LastSelectedRepos { get; init; } = [];
    public List<Guid> LastSelectedProfiles { get; init; } = [];
    public List<LocalInstance> LocalInstances { get; init; } = [];
}
