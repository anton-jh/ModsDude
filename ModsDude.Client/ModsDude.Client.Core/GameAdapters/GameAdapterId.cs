namespace ModsDude.Client.Core.GameAdapters;

public readonly record struct GameAdapterId
{
    private const string _separator = "__";


    public GameAdapterId(string id, int compatibilityVersion)
    {
        if (id.Contains(_separator))
        {
            throw new ArgumentException($"Id cannot contain the separator: '{_separator}'");
        }

        Id = id;
        CompatibilityVersion = compatibilityVersion;
    }


    public string Id { get; }
    public int CompatibilityVersion { get; }


    public override readonly string ToString()
    {
        return $"{Id}{_separator}{CompatibilityVersion}";
    }


    public static GameAdapterId Parse(string s)
    {
        return s.Split(_separator) switch
        {
            [var id, var version] when int.TryParse(version, out var parsedVersion) => new(id, parsedVersion),
            _ => throw new FormatException()
        };
    }

    public static bool TryParse(string s, out GameAdapterId result)
    {
        var parts = s.Split(_separator);
        if (parts is [var id, var version] && int.TryParse(version, out var parsedVersion))
        {
            result = new(id, parsedVersion);
            return true;
        }
        result = default;
        return false;
    }
}
