namespace ModsDude.WindowsClient.Model.GameAdapters;

public readonly record struct GameAdapterId(string Id, string CompatibilityVersion)
{
    private const string _separator = "__";


    public override readonly string ToString()
    {
        return $"{Id}{_separator}{CompatibilityVersion}";
    }


    public static GameAdapterId Parse(string s)
    {
        return s.Split(_separator) switch
        {
            [var id, var version] => new(id, version),
            _ => throw new FormatException()
        };
    }

    public static bool TryParse(string s, out GameAdapterId result)
    {
        var parts = s.Split(_separator);
        if (parts is [var id, var version])
        {
            result = new(id, version);
            return true;
        }
        result = default;
        return false;
    }
}
