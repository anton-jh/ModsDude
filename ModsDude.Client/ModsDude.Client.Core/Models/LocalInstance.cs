namespace ModsDude.Client.Core.Models;

public interface ILocalInstance
{
    Guid Id { get; }
    string Name { get; set; }
    string UserId { get; }
    Guid RepoId { get; }
}

public class LocalInstance<TBaseSettings, TInstanceSettings>
    : ILocalInstance
{
    public required Guid Id { get; init; }
    public required string UserId { get; init; }
    public required Guid RepoId { get; init; }
    public required string Name { get; set; }
}
