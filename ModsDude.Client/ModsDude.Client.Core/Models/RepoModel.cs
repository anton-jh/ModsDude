using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;

namespace ModsDude.Client.Core.Models;
public class RepoModel
{
    public required Guid Id { get; init; }
    public required string Name { get; set; }
    public required GameAdapterId AdapterId { get; init; }
    public required DynamicForm AdapterConfiguration { get; init; }
}
