using ModsDude.Client.Core.GameFiles;

namespace ModsDude.Client.Core.Sync;

public interface IManagedFileDriftCheck
{
    /// <summary>
    /// The managed files an apply of what the manifest records would change now, or null where
    /// there is no adapter to ask.
    /// </summary>
    IReadOnlyList<PlannedFileEdit>? FindOutOfLine(SyncManifest manifest);
}
