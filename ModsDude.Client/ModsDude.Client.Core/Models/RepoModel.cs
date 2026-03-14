namespace ModsDude.Client.Core.Models;
public class RepoModel
{
    public required Guid Id { get; init; }
    public required string Name { get; set; }
    public required string? AdapterId { get; init; }
    public required string? AdapterConfiguration { get; init; }

    public List<ILocalInstance> LocalInstances { get; init; } = [];
}
