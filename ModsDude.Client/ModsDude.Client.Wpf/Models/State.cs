namespace ModsDude.Client.Wpf.Models;

internal class State
{
    public int Version { get; set; } = 1;
    public List<Guid> LastSelectedRepos { get; init; } = [];
    public List<Guid> LastSelectedProfiles { get; init; } = [];
}
