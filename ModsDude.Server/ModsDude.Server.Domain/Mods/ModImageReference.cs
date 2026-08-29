namespace ModsDude.Server.Domain.Mods;

public class ModImageReference(
    string hash,
    ModImageKind kind,
    int position,
    string fileName)
{
    public string Hash { get; init; } = hash;
    public ModImageKind Kind { get; init; } = kind;
    public int Position { get; init; } = position;
    public string FileName { get; init; } = fileName;
}


public enum ModImageKind
{
    Icon,
    StoreImage
}
