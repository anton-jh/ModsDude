namespace ModsDude.WindowsClient.Model.Models;
public class LocalInstance
{
    public required Guid Id { get; init; }
    public required string UserId { get; init; }
    public required Guid RepoId { get; init; }
    public required string Name { get; set; }
}
