namespace ModsDude.Client.Core.Persistence;

public class PersistedLocalInstance
{
    public required Guid Id { get; init; }
    public required Guid RepoId { get; init; }
    public required string Name { get; set; }
    public required string AdapterInstanceSettings { get; set; }
}
