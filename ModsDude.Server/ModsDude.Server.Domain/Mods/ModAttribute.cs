namespace ModsDude.Server.Domain.Mods;
public class ModAttribute(
    string key,
    string? value = null)
{
    public string Key { get; init; } = key;
    public string? Value { get; set; } = value;
}
