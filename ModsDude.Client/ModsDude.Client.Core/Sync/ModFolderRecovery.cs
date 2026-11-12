using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Helpers;

namespace ModsDude.Client.Core.Sync;

/// <summary>
/// Undoes what an apply killed part way left in a mod folder, so the next plan sees the folder as it
/// was before that apply rather than as a crash left it.
/// </summary>
/// <remarks>
/// A staging copy is always a copy of something still held elsewhere, so it is deleted. A file part
/// way through a two-step rename is the only copy of a mod, so it goes back under the name it was
/// being given - or to the Recycle Bin, where something already holds that name.
/// </remarks>
internal static class ModFolderRecovery
{
    private const string _renamingExtension = ".modsdude-renaming";


    /// <summary>Where a file waits between the two steps of a rename to <paramref name="destination"/>.</summary>
    public static string GetRenamingPath(string destination) => destination + _renamingExtension;

    public static void Recover(string modFolder, IRecycleBin recycleBin, ILogger log)
    {
        foreach (var file in Directory.EnumerateFiles(modFolder).Order(StringComparer.Ordinal))
        {
            if (file.EndsWith(FileSystemHelper.StagingExtension, StringComparison.OrdinalIgnoreCase))
            {
                log.LogInformation("Deleting {File}, a copy an interrupted apply left behind.", file);

                FileSystemHelper.TryDeleteFile(file, log);
            }
            else if (file.EndsWith(_renamingExtension, StringComparison.OrdinalIgnoreCase))
            {
                RestoreRenaming(file, recycleBin, log);
            }
        }
    }


    private static void RestoreRenaming(string file, IRecycleBin recycleBin, ILogger log)
    {
        var wanted = file[..^_renamingExtension.Length];

        if (File.Exists(wanted) is false)
        {
            log.LogInformation("Restoring {File}, which an interrupted apply left part way through a rename.", wanted);

            File.Move(file, wanted);

            return;
        }

        if (recycleBin.TryRecycle(file) is false)
        {
            throw new IOException($"'{Path.GetFileName(file)}' was left by an interrupted apply and could not be moved to the Recycle Bin.");
        }

        log.LogInformation("Recycled {File}, which an interrupted apply left behind beside a file of the same name.", file);
    }
}
