namespace ModsDude.Client.Core.Sync;

public interface IDriftMonitor : IDisposable
{
    /// <summary>Raised after any check that changed what the notice would say.</summary>
    event EventHandler? Changed;

    /// <summary>Every game that reported drift, most recently checked first.</summary>
    IReadOnlyList<TargetDrift> Drifted { get; }

    bool HasDrift { get; }

    /// <summary>Every rewritten store blob this session has caught. See <see cref="IStoreIntegrityService.Found"/>.</summary>
    IReadOnlyList<CorruptedBlob> StoreCorruption { get; }

    bool HasStoreCorruption { get; }

    /// <summary>Whether the check found anything at all worth building a notice out of.</summary>
    /// <remarks>
    /// <b>It says nothing about whether anything is on screen.</b> Dismissal is per notice and belongs
    /// to the shell - <see cref="Notices.DismissalLedger"/> - so this monitor reports facts and has no
    /// opinion about what the user has read.
    /// </remarks>
    bool HasAnything { get; }

    /// <summary>
    /// Runs the cheap check across every game, off the calling thread, since it lists directories
    /// and hashes savegame slots.
    /// </summary>
    /// <remarks>
    /// Checks run one at a time. A request made while one runs is answered by a single further run
    /// after it, shared by every request made meanwhile, so the newest answer is always the one left
    /// standing. Never faults: a failed check is logged and leaves the last answer in place.
    /// </remarks>
    /// <returns>False where the throttle swallowed the request, so nothing was looked at.</returns>
    Task<bool> CheckAsync(DriftCheckReason reason = DriftCheckReason.Explicit);

    /// <summary><see cref="CheckAsync"/> for a caller that does not wait for the answer.</summary>
    void RequestCheck(DriftCheckReason reason = DriftCheckReason.Explicit);

    /// <summary>
    /// A latency optimisation on top of the manifest comparison, for the narrower case where ModsDude
    /// happens to be open while the mods change. It decides nothing on its own - watchers miss events
    /// across sleep and on network paths, and the design must not depend on having been running.
    /// </summary>
    void Watch();
}
