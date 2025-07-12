namespace ModsDude.Client.Core.GameAdapters;

public readonly record struct GameAdapterId
{
    private const string _separator = "__";


    public GameAdapterId(string id, string compatibilityVersion)
    {
        if (id.Contains(_separator) || compatibilityVersion.Contains(_separator))
        {
            throw new ArgumentException($"Id or compatibilityVersion parts cannot contain the separator: '{_separator}'");
        }

        Id = id;
        CompatibilityVersion = compatibilityVersion;
    }


    public string Id { get; }
    public string CompatibilityVersion { get; }


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
