using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using System.Text.Json;

namespace ModsDude.Client.Core.GameAdapters.Implementations.BeamNgV1;

/// <summary>BeamNG.drive, played alone or through BeamMP. Mods only.</summary>
public class BeamNgGameAdapter(ILoggerFactory? loggerFactory = null) : IGameAdapter
{
    /// <summary>
    /// Handed down to the capability adapters, which read files somebody else wrote and degrade
    /// rather than throw when one will not parse - so without this the degrading is invisible.
    /// </summary>
    protected ILoggerFactory Loggers { get; } = loggerFactory ?? NullLoggerFactory.Instance;

    public GameAdapterId Id { get; } = new("_beamng_drive", 1);
    public string DisplayName { get; } = "BeamNG.drive";
    public string ShortName { get; } = "BNG";
    public string Description { get; } = "For BeamNG.drive, with or without BeamMP.";


    public DynamicForm GetBaseSettingsTemplate()
    {
        return new EmptyAdapterSettings();
    }

    public IBaseGameAdapter WithBaseSettings(string serializedBaseSettings)
    {
        var settings = JsonSerializer.Deserialize<EmptyAdapterSettings>(serializedBaseSettings)
            ?? throw new ArgumentException("Could not deserialize base settings");

        return new BeamNgBaseGameAdapter(settings, Loggers);
    }

    public IBaseGameAdapter WithBaseSettings(DynamicForm baseSettings)
    {
        if (baseSettings is not EmptyAdapterSettings settings)
        {
            throw new IncorrectGameAdapterSettingsTypeException<EmptyAdapterSettings>(baseSettings);
        }

        return new BeamNgBaseGameAdapter(settings, Loggers);
    }
}
