namespace ModsDude.Client.Core.Sync;

public interface IContentStoreMaintenance
{
    /// <summary>
    /// Every store on this machine: the ones serving a mod folder now, and the ones settings still
    /// name.
    /// </summary>
    /// <remarks>
    /// Both halves are needed and neither is a superset of the other.
    /// <see cref="IContentStoreProvider.GetAllStores"/> reads settings, so it misses the store a
    /// mod folder is served by under the defaults nobody has visited a page to accept; and a store
    /// whose disk no longer holds any game is exactly the one worth being able to empty, so it
    /// cannot be dropped for having nothing to serve.
    /// </remarks>
    IReadOnlyList<ContentStore> GetStores();

    /// <summary>
    /// Trims every store on this machine back inside its size limit. Nobody asks for this.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Keeping a store inside the size limit is the app's job, not the user's.</b> A limit somebody
    /// typed once is a promise the app makes, and a button labelled "trim" is that promise handed back
    /// to them to keep - which also means it is only kept by the users who noticed the button.
    /// </para>
    /// <para>
    /// The sweep at the end of every apply does most of it, but it only ever touches the store that
    /// apply was using, so three gaps were left standing: a store that no longer serves any mod folder
    /// is never swept at all, an import seeds bytes into a store without any sync following it, and a
    /// limit lowered in settings does nothing until the next apply happens to that disk. This is what
    /// closes them, and it runs on the events that open them - startup, a finished import, a saved
    /// settings page.
    /// </para>
    /// <para>
    /// <b>Skips a busy store rather than waiting for it</b>, exactly as the sync's own sweep does and
    /// for the same reason: nobody is watching this, so blocking an apply to reclaim space a moment
    /// sooner is the wrong trade. Everything in a store is registered in a repo and re-downloadable,
    /// so a store missed here is swept by the next trigger.
    /// </para>
    /// <para>
    /// What each served game is running comes off its sync manifest, so this asks the network nothing
    /// and works offline.
    /// </para>
    /// </remarks>
    /// <returns>How many bytes were given back, across every store that needed it.</returns>
    Task<long> SweepAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gives back everything a store is costing, keeping what the mod folders it serves are running.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not the same as emptying it</b> - see <see cref="ContentStore.Reclaim"/>. What a mod folder
    /// on this disk already holds costs the store no bytes of its own, so dropping it would free
    /// nothing and only guarantee a re-download.
    /// </para>
    /// <para>
    /// <b>Waits for the store, where <see cref="SweepAllAsync"/> skips it.</b> The difference is who
    /// asked: tidying is housekeeping nobody requested and the next trigger will do it anyway, whereas
    /// somebody pressing this is waiting for an answer, and refusing them on the grounds that a sync
    /// is running would only send them back to press it again. <paramref name="onWaiting"/> is how
    /// that wait gets said out loud.
    /// </para>
    /// </remarks>
    Task<ContentStoreClearResult> ReclaimAsync(
        ContentStore store,
        CancellationToken cancellationToken,
        Action? onWaiting = null);

    /// <summary>
    /// Sends the store's quarantine folder - files nothing else has a copy of - to the Recycle Bin.
    /// </summary>
    /// <inheritdoc cref="ReclaimAsync" path="/remarks/para[2]"/>
    /// <returns>
    /// The bytes it held, or null where the Recycle Bin would not take it - which is common, since a
    /// disk without one is how files end up in quarantine - and the folder is left as it was.
    /// </returns>
    Task<long?> RecycleQuarantineAsync(
        ContentStore store,
        CancellationToken cancellationToken,
        Action? onWaiting = null);

    /// <summary>
    /// Reads every blob in a store, drops the ones that no longer match their address, and says which
    /// mod folders are still running them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The half a store cannot answer for itself. <see cref="ContentStore.VerifyAllAsync"/> reports
    /// addresses, because a store deliberately knows nothing about what a file <em>is</em>; the
    /// manifests here turn those into folders and profile names. That matters because <b>removing the
    /// blob is not the repair</b>. Where the entry was hardlinked into a mod folder, that folder is
    /// still holding the same wrong bytes under the same name, and only re-applying its profile
    /// replaces them - so a pass that found something has to be able to say where to go next.
    /// </para>
    /// <para>
    /// Answered from the manifests, so it asks the network nothing and works offline - the same
    /// bargain <see cref="SweepAllAsync"/> makes.
    /// </para>
    /// </remarks>
    /// <inheritdoc cref="ReclaimAsync" path="/remarks/para[2]"/>
    Task<StoreVerificationReport> VerifyAsync(
        ContentStore store,
        IProgress<ContentStoreVerificationProgress>? progress,
        CancellationToken cancellationToken,
        Action? onWaiting = null);
}
