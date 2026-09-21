namespace ModsDude.Client.Core;

/// <summary>
/// Which install of the app this process is, and therefore which files on disk are its own.
/// </summary>
/// <remarks>
/// <para>
/// <b>One name, and everything private to an install is filed under it</b>: <c>state.json</c>, the
/// sync manifests, the logs, the sign-in token cache, the default content store and the default image
/// cache. A debug build run from the IDE and the build somebody actually uses are then two apps that
/// cannot see, let alone damage, each other's settings - the only things they still share are the mod
/// folders and savegames they are both pointed at, which is the user's to keep straight.
/// </para>
/// <para>
/// <b>Production is the bare name.</b> It is what an installed copy is, and what every existing
/// <c>%LocalAppData%\ModsDude</c> already belongs to; anything else is suffixed with its environment
/// so two of them can never collide.
/// </para>
/// <para>
/// <b>Set once, before anything reads a path.</b> Static because the paths it decides are read from
/// static helpers all over the client, and strict because the failure mode of a late call is a store
/// already opened in the wrong folder: <see cref="Initialize"/> refuses once the name has been used,
/// which turns that from a corrupted install into an exception on the first launch.
/// </para>
/// </remarks>
public static class AppIdentity
{
    public const string ProductionEnvironment = "Production";
    private const string _product = "ModsDude";

    private static string _environmentName = ProductionEnvironment;
    private static bool _used;


    /// <summary>The environment this process runs as. <see cref="ProductionEnvironment"/> until told otherwise.</summary>
    public static string EnvironmentName => _environmentName;

    public static bool IsProduction => IsProductionEnvironment(_environmentName);

    /// <summary>
    /// The folder name every install-private path is filed under.
    /// </summary>
    public static string Name
    {
        get
        {
            _used = true;

            return NameFor(_environmentName);
        }
    }

    /// <summary>What a person should see on the window and the tray icon: nothing extra in production.</summary>
    public static string DisplayName => DisplayNameFor(_environmentName);

    public static string DisplayNameFor(string environmentName)
        => IsProductionEnvironment(environmentName) ? _product : $"{_product} ({environmentName.Trim()})";


    /// <exception cref="InvalidOperationException">Where a path has already been built from the old name.</exception>
    public static void Initialize(string environmentName)
    {
        if (_used)
        {
            throw new InvalidOperationException(
                "The app identity was changed after something had already used it; it has to be set first.");
        }

        _environmentName = string.IsNullOrWhiteSpace(environmentName)
            ? ProductionEnvironment
            : environmentName.Trim();
    }

    public static string NameFor(string environmentName)
        => IsProductionEnvironment(environmentName) ? _product : $"{_product}.{environmentName.Trim()}";


    private static bool IsProductionEnvironment(string environmentName)
        => string.IsNullOrWhiteSpace(environmentName)
            || string.Equals(environmentName.Trim(), ProductionEnvironment, StringComparison.OrdinalIgnoreCase);
}
