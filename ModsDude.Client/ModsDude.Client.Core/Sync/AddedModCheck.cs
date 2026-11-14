using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.Helpers;

namespace ModsDude.Client.Core.Sync;

/// <summary>
/// Asks the adapter which added files are mods, so a readme or a broken download put in the mod
/// folder is not reported as a mod to import.
/// </summary>
/// <remarks>
/// Opens only the added files, which on an ordinary check is none of them.
/// </remarks>
public sealed class AddedModCheck(
    IModTargetAdapters adapters,
    ILogger<AddedModCheck> logger) : IAddedModCheck
{
    public async Task<IReadOnlyList<string>> FindModsAsync(SyncManifest manifest, IReadOnlyList<string> fileNames, CancellationToken cancellationToken)
    {
        if (fileNames.Count == 0)
        {
            return [];
        }

        ResolvedModTarget? resolved;

        try
        {
            resolved = adapters.Find(manifest.Target);
        }
        catch (UserFriendlyException exception)
        {
            logger.LogWarning(exception, "Could not build the adapter for {Target} to read the files added to its mod folder.", manifest.Target);

            return fileNames;
        }

        // A target the settings now put somewhere else would be scanned in the wrong folder, where
        // none of these files are.
        if (resolved is null || FileSystemHelper.ArePathsEqual(resolved.Target.Path, manifest.ModFolder) is false)
        {
            return fileNames;
        }

        var added = fileNames
            .Select(x => Path.GetFullPath(Path.Combine(manifest.ModFolder, x)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        try
        {
            var mods = await resolved.Adapter.GetInstalledMods(
                resolved.Target,
                x => added.Contains(Path.GetFullPath(x)) is false,
                cancellationToken);

            var found = mods
                .Select(x => Path.GetFileName(x.FilePath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return [.. fileNames.Where(found.Contains)];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not read the files added to the mod folder {Folder}.", manifest.ModFolder);

            return fileNames;
        }
    }
}
