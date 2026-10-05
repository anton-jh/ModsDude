using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameFiles;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Sync;

/// <summary>
/// Whether a target's managed files still say what the last apply made them say.
/// </summary>
/// <remarks>
/// <para>
/// <b>Asked of the edits, not of the bytes.</b> A game rewrites its own files for reasons of its
/// own - a mod list stamped with access times on every launch - and a file that changed without its
/// meaning changing is not drift. So the layout is worked out again for the mods the manifest says
/// are installed, and each edit is asked whether it would change its file today.
/// </para>
/// <para>
/// Reads one small file per edit and no archive, which is what lets it run on every drift check.
/// </para>
/// </remarks>
public sealed class ManagedFileDriftCheck(
    IModTargetAdapters adapters,
    IGameFileEditor fileEditor,
    ILogger<ManagedFileDriftCheck> logger) : IManagedFileDriftCheck
{
    public IReadOnlyList<PlannedFileEdit>? FindOutOfLine(SyncManifest manifest)
    {
        if (manifest.ManagedFiles.Count == 0)
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
            logger.LogWarning(exception, "Could not build the adapter for {Target} to check its managed files.", manifest.Target);

            return null;
        }

        if (resolved is null)
        {
            return null;
        }

        var layout = resolved.Adapter.Layout(new ModLayoutContext(
            resolved.Target,
            [.. manifest.Entries.Select(x =>
            {
                var modId = ModKey.From(x.ModId);

                return new ModLayoutMod(
                    modId,
                    ModVersionKey.From(x.VersionId),
                    x.ContentHash,
                    ModFileName.For(modId, x.FileName),
                    x.FileName,
                    x.Locked);
            })]));

        var outOfLine = new List<PlannedFileEdit>();

        foreach (var edit in layout.ManagedFiles)
        {
            try
            {
                var planned = fileEditor.Plan(manifest.ModFolder, edit);

                if (planned.Changes)
                {
                    outOfLine.Add(planned);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A file the edit cannot read - half-written by the game, or rewritten in a shape the
                // adapter does not know - is one an apply would refuse over, which is out of line.
                logger.LogWarning(exception, "Could not check the managed file {File} in {Folder}.", edit.RelativePath, manifest.ModFolder);

                outOfLine.Add(new PlannedFileEdit(edit, Path.GetFullPath(Path.Combine(manifest.ModFolder, edit.RelativePath)), true));
            }
        }

        return outOfLine;
    }
}
