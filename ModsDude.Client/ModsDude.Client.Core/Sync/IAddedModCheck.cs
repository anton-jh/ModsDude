namespace ModsDude.Client.Core.Sync;

public interface IAddedModCheck
{
    /// <summary>
    /// Which of these files, added to the manifest's folder since the last apply, the adapter reads
    /// as mods. All of them where there is no adapter to ask or it could not answer: unknown is
    /// reported, not hidden.
    /// </summary>
    Task<IReadOnlyList<string>> FindModsAsync(SyncManifest manifest, IReadOnlyList<string> fileNames, CancellationToken cancellationToken);
}
