using ModsDude.Client.Core.GameAdapters.DynamicForms;

namespace ModsDude.Client.Core.Models;

public class LocalInstance
{
    public required Guid Id { get; init; }
    public required Guid RepoId { get; init; }
    public required string Name { get; set; }
    public required DynamicForm AdapterInstanceSettings { get; set; }
}
