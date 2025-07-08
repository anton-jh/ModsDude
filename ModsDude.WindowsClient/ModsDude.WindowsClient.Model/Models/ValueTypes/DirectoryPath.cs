namespace ModsDude.WindowsClient.Model.Models.ValueTypes;
public readonly record struct DirectoryPath(string Value)
{
    public override string ToString()
    {
        return Value;
    }
}
