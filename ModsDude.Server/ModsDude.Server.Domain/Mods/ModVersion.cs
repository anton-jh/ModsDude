namespace ModsDude.Server.Domain.Mods;

public class ModVersion
{
    internal ModVersion() { }


    public required ModVersionId Id { get; init; }

    public required Mod Mod { get; init; }


    public required int SequenceNumber { get; set; }
    public required string DisplayName { get; set; }
    public required string Description { get; set; }

    public required HashSet<ModAttribute> Attributes { get; init; }

    public required DateTimeOffset Created { get; init; }
}


public readonly record struct ModVersionId(string Value);
