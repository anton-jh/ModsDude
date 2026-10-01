namespace ModsDude.Client.Core.Sync;

public interface IDriftMonitor : IDisposable
{
    /// <summary>Raised after any check that changed what the notice would say.</summary>
    event EventHandler? Changed;

    /// <summary>Every game that reported drift, most recently checked first.</summary>
    IReadOnlyList<TargetDrift> Drifted { get; }

    bool HasDrift { get; }

    /// <summary>
    /// Every rewritten store blob this session has caught, whether or not the check that found it was
    /// the most recent one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Accumulated rather than recomputed</b>, which is the opposite of how everything else here
    /// works and is the point. A corrupt blob is deleted the moment it is found, so the very next
    /// check cannot see it - the evidence destroys itself, by design, because leaving it would go on
    /// serving wrong bytes to every repo on the volume. A finding that vanished on the next alt-tab
    /// would be one nobody ever read.
    /// </para>
    /// <para>
    /// It also means something bigger than the mod it names: the game's updater wrote through a
    /// hardlink, which is the assumption <c>SupportsHardlinks</c> is set on. That is worth keeping on
    /// screen until somebody waves it away.
    /// </para>
    /// </remarks>
    IReadOnlyList<CorruptedBlob> StoreCorruption { get; }

    bool HasStoreCorruption { get; }

    /// <summary>Whether the check found anything at all worth building a notice out of.</summary>
    /// <remarks>
    /// <b>It says nothing about whether anything is on screen.</b> Dismissal used to live here as one
    /// signature over every drifted folder and every corrupt blob at once, because there was one card
    /// with one button on it. It is now per notice and belongs to the shell -
    /// <see cref="Notices.DismissalLedger"/> - so this monitor reports facts and has no opinion about
    /// what the user has read.
    /// </remarks>
    bool HasAnything { get; }

    /// <summary>
    /// Runs the cheap check across every game and reports whether the answer changed.
    /// </summary>
    /// <returns>False where the throttle swallowed the request, so nothing was looked at.</returns>
    bool Check(DriftCheckReason reason = DriftCheckReason.Explicit);

    /// <summary>
    /// The same check off the calling thread, since it lists directories - and now hashes savegame
    /// slots - which may be slow.
    /// </summary>
    /// <remarks>
    /// <see cref="Task.Run{TResult}(Func{Task{TResult}})"/> rather than awaiting the core directly, so that the
    /// whole of it - including the synchronous directory listings before the first await - is off the
    /// caller's thread, and so that <see cref="Check"/> can block on it from a UI thread without the
    /// continuations queueing behind the block it is itself holding.
    /// </remarks>
    Task<bool> CheckAsync(DriftCheckReason reason = DriftCheckReason.Explicit);

    /// <summary>
    /// A latency optimisation on top of the manifest comparison, for the narrower case where ModsDude
    /// happens to be open while the mods change. It decides nothing on its own - watchers miss events
    /// across sleep and on network paths, and the design must not depend on having been running.
    /// </summary>
    void Watch();
}
