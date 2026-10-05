using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters.DynamicForms;

namespace ModsDude.Client.Core.GameAdapters.Implementations.BeamNgV1;

public class BeamNgBaseGameAdapter(EmptyAdapterSettings settings, ILoggerFactory loggerFactory)
    : BeamNgGameAdapter(loggerFactory), IBaseGameAdapter
{
    private readonly List<object> _capabilities = [
        new Func<IBaseModAdapter>(() => new BeamNgBaseModAdapter(loggerFactory))
        ];


    public DynamicForm BaseSettings { get; } = settings;

    public bool CanSupportMods { get; } = true;
    public bool CanSupportSavegames { get; } = false;

    /// <summary>
    /// The game as Steam starts it and as it runs, and BeamMP's launcher and server. One list for
    /// every folder: a BeamMP server running on this machine stops a sync to the game's own folder
    /// too.
    /// </summary>
    public IReadOnlyList<string> ProcessNames { get; } = ["BeamNG.drive", "BeamNG.drive.x64", "BeamMP-Launcher", "BeamMP-Server"];


    public DynamicForm DeserializeLocalSettings(string serializedLocalSettings)
    {
        return BeamNgLocalSettings.Deserialize(serializedLocalSettings);
    }

    public Func<T>? GetBaseCapabilityAdapterFactory<T>()
    {
        return _capabilities.OfType<Func<T>>().SingleOrDefault();
    }

    /// <summary>Filled in with whichever of the usual folders this machine has.</summary>
    public DynamicForm GetLocalSettingsTemplate()
    {
        return BeamNgLocalSettings.WithDetectedFolders(Loggers.CreateLogger<BeamNgBaseGameAdapter>());
    }

    public ILocalGameAdapter WithLocalSettings(string serializedLocalSettings)
    {
        return WithLocalSettings(BeamNgLocalSettings.Deserialize(serializedLocalSettings));
    }

    public ILocalGameAdapter WithLocalSettings(DynamicForm localSettings)
    {
        if (localSettings is not BeamNgLocalSettings local)
        {
            throw new IncorrectGameAdapterSettingsTypeException<BeamNgLocalSettings>(localSettings);
        }

        return new BeamNgLocalGameAdapter(settings, local, Loggers);
    }
}
