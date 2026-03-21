namespace ModsDude.Client.Core.Models;

public class LocalInstance
{
    public required Guid RepoId { get; init; }
    public required string Name { get; set; }
    public required string AdapterInstanceSettings { get; set; }
}
