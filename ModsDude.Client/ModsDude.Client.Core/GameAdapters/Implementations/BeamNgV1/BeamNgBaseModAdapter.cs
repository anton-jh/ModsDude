using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.GameAdapters.Implementations.BeamNgV1;

public class BeamNgBaseModAdapter(ILoggerFactory? loggerFactory = null) : IBaseModAdapter
{
    /// <summary>
    /// A folder scan skips what it cannot read, which is correct and also silent - in a mod folder
    /// every skip is a mod that has vanished from the catalog.
    /// </summary>
    protected ILogger Log { get; } = loggerFactory?.CreateLogger<BeamNgBaseModAdapter>()
        ?? NullLogger<BeamNgBaseModAdapter>.Instance;

    protected ILoggerFactory? Loggers { get; } = loggerFactory;


    /// <inheritdoc/>
    public IReadOnlyList<ModAttributeDefinition> Attributes => BeamNgModAttributes.Definitions;

    /// <summary>Not read while the game has no savegames here; the same weights as any long mod list.</summary>
    public SavegameCompatibilityPolicy SavegameCompatibility { get; } = new(
        AddedWeight: 1,
        ChangedWeight: 1,
        RemovedWeight: 5,
        PromptThreshold: 50);


    public Task<IEnumerable<LocalMod>> GetModsFromFolder(string path, CancellationToken cancellationToken)
        => ReadFolder(path, _ => false, Path.GetFileName, cancellationToken);

    /// <param name="skip">Files the caller already knows, left unopened. See <see cref="ILocalModAdapter.GetInstalledMods"/>.</param>
    /// <param name="nameOf">The name a file's mod goes by, from the file's own.</param>
    protected Task<IEnumerable<LocalMod>> ReadFolder(
        string path,
        Func<string, bool> skip,
        Func<string, string> nameOf,
        CancellationToken cancellationToken)
    {
        return ModArchives.ReadFolderAsync(
            path,
            skip,
            (file, ct) => BeamNgModArchive.Read(file, nameOf(Path.GetFileName(file)), Log, ct),
            cancellationToken);
    }

    public ILocalModAdapter WithLocalSettings(string serializedLocalSettings)
    {
        return WithLocalSettings(BeamNgLocalSettings.Deserialize(serializedLocalSettings));
    }

    public ILocalModAdapter WithLocalSettings(DynamicForm localSettings)
    {
        if (localSettings is not BeamNgLocalSettings settings)
        {
            throw new IncorrectGameAdapterSettingsTypeException<BeamNgLocalSettings>(localSettings);
        }

        return new BeamNgLocalModAdapter(settings, Loggers);
    }
}
