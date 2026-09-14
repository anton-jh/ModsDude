namespace ModsDude.Client.Core.Concurrency;

/// <summary>
/// One claim on one named resource, held for exactly as long as the work is.
/// </summary>
/// <remarks>
/// <b>Dispose releases it</b>, so a <c>using</c> covers the failure and cancellation paths without a
/// <c>finally</c> at every call site - which is the way a resource stays locked until the app is
/// restarted. Disposing twice is legitimate and does nothing the second time.
/// </remarks>
public interface IResourceLease : IDisposable
{
    /// <summary>What is held. A composite lease reports its keys joined, for a log line.</summary>
    string Key { get; }

    bool IsExclusive { get; }
}


/// <summary>
/// Who is allowed to touch what, while something long-running is touching it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The lock is on the resource, not on the user's attention.</b> A modal would guard the wrong
/// thing: an apply reached from the drift notice goes through no page at all, so a dialog over the
/// page it did not come from protects nothing - while a mod folder somebody is browsing is perfectly
/// safe to browse. So what is claimed here is the thing that can actually be left in a state nothing
/// describes: a mod folder, a repo, a content store. See docs/07-mod-sync-design.md#leases.
/// </para>
/// <para>
/// <b>Keyed by name rather than by object</b>, because two of the three resources have no object to
/// lock on. <see cref="Sync.ContentStoreProvider"/> builds a fresh <see cref="Sync.ContentStore"/> on
/// every call - it is a handle over a directory, not a singleton - so a field inside it would guard a
/// different instance each time. <see cref="ResourceKeys"/> is the only place keys are spelled, so
/// two call sites cannot disagree about what they are claiming.
/// </para>
/// <para>
/// <b>Shared and exclusive rather than a mutex</b>, because the store's additive half is already safe
/// by construction - content-addressed paths, hash verified before placement - and only deletion
/// racing installation is not. Serialising the installs too would be a real cost for no gain. See
/// the remarks on <see cref="AcquireSharedAsync"/>.
/// </para>
/// <para>
/// <b>Try rather than wait, wherever a person is holding the mouse.</b> A button that greys out and a
/// sentence saying what is running beats a click that appears to do nothing for four minutes, so the
/// apply path asks <see cref="TryAcquireExclusive(IEnumerable{string}, string)"/> and reports a
/// refusal. Waiting is for the housekeeping that can afford it.
/// </para>
/// </remarks>
public interface IResourceLeases
{
    /// <summary>
    /// Raised whenever anything is claimed or released, so a <c>CanExecute</c> can be re-asked.
    /// </summary>
    /// <remarks>
    /// <b>Fired from whichever thread finished the work</b>, which is nearly never the UI one. Every
    /// subscriber marshals for itself; doing it here would mean the release of a lease depended on a
    /// dispatcher being alive, and releasing must never be the thing that fails.
    /// </remarks>
    event EventHandler? Changed;

    /// <summary>
    /// Claims every one of <paramref name="keys"/> exclusively, or claims none of them.
    /// </summary>
    /// <remarks>
    /// <b>All or nothing, and never waiting.</b> A game reaching three mod folders is one gesture over
    /// three resources, and holding two of them while something else holds the third is a gesture that
    /// can neither proceed nor be described. Null means somebody else is mid-gesture -
    /// <see cref="DescribeHolder"/> says who, which is the sentence worth showing.
    /// </remarks>
    IResourceLease? TryAcquireExclusive(IEnumerable<string> keys, string holder);

    /// <inheritdoc cref="TryAcquireExclusive(IEnumerable{string}, string)"/>
    IResourceLease? TryAcquireExclusive(string key, string holder);

    /// <summary>
    /// Claims <paramref name="keys"/> alongside everything else that is only reading them, waiting for
    /// anything that wanted them to itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The content store's shape. Installing from a store and ingesting into it are safe to run
    /// beside each other however many syncs are going - every path in is verified and written under
    /// its own hash - so those take this. What is <em>not</em> safe beside them is
    /// <see cref="Sync.ContentStore.Evict"/> and the two clears, which delete; those take the
    /// exclusive side.
    /// </para>
    /// <para>
    /// Waits rather than refuses, because the thing it waits for is bounded housekeeping and the
    /// caller is a sync that is already minutes long. Cancellation is the way out.
    /// </para>
    /// </remarks>
    Task<IResourceLease> AcquireSharedAsync(IEnumerable<string> keys, string holder, CancellationToken cancellationToken);

    /// <summary>
    /// Claims <paramref name="key"/> to the exclusion of everything, waiting however long that takes.
    /// </summary>
    /// <remarks>
    /// For the housekeeping a person asked for and is watching - a sweep, a clear, a verify pass. It
    /// waits because refusing "empty this store" on the grounds that a sync is running would send
    /// somebody back to press the same button again; the sync is finite and the button said it was
    /// going to take a while.
    /// </remarks>
    Task<IResourceLease> AcquireExclusiveAsync(string key, string holder, CancellationToken cancellationToken);

    /// <summary>What is holding <paramref name="key"/>, named for the person waiting. Null when free.</summary>
    string? DescribeHolder(string key);

    bool IsHeld(string key);

    /// <summary>
    /// Everything currently held, named. For the one question the shell asks: is now a bad moment to
    /// close the window.
    /// </summary>
    IReadOnlyList<string> DescribeAll();
}


/// <inheritdoc cref="IResourceLeases"/>
public sealed class ResourceLeases : IResourceLeases
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Resource> _resources = new(StringComparer.Ordinal);


    public event EventHandler? Changed;


    public IResourceLease? TryAcquireExclusive(string key, string holder)
    {
        return TryAcquireExclusive([key], holder);
    }

    public IResourceLease? TryAcquireExclusive(IEnumerable<string> keys, string holder)
    {
        var ordered = Order(keys);

        if (ordered.Count == 0)
        {
            return Empty(holder);
        }

        lock (_gate)
        {
            // Asked about every key before any of them is taken, so a refusal leaves nothing behind
            // to unwind and nothing visible to anybody watching the event. Read without creating an
            // entry, so refusing does not leave a name this dictionary then remembers forever.
            foreach (var key in ordered)
            {
                if (_resources.TryGetValue(key, out var resource) && CanEnterNow(resource, exclusive: true) is false)
                {
                    return null;
                }
            }

            foreach (var key in ordered)
            {
                Enter(Resolve(key), exclusive: true, holder);
            }
        }

        Raise();

        return Composite(ordered, exclusive: true, holder);
    }

    public async Task<IResourceLease> AcquireSharedAsync(
        IEnumerable<string> keys,
        string holder,
        CancellationToken cancellationToken)
    {
        return await AcquireAsync(keys, exclusive: false, holder, cancellationToken);
    }

    public async Task<IResourceLease> AcquireExclusiveAsync(
        string key,
        string holder,
        CancellationToken cancellationToken)
    {
        return await AcquireAsync([key], exclusive: true, holder, cancellationToken);
    }

    public string? DescribeHolder(string key)
    {
        lock (_gate)
        {
            return _resources.TryGetValue(key, out var resource) ? resource.Holders.FirstOrDefault() : null;
        }
    }

    public bool IsHeld(string key)
    {
        lock (_gate)
        {
            return _resources.TryGetValue(key, out var resource) && resource.IsHeld;
        }
    }

    public IReadOnlyList<string> DescribeAll()
    {
        lock (_gate)
        {
            return [.. _resources.Values.SelectMany(x => x.Holders).Distinct(StringComparer.Ordinal)];
        }
    }


    /// <summary>
    /// Takes the keys in a fixed order, one at a time, waiting for each.
    /// </summary>
    /// <remarks>
    /// <b>Sorted, which is the whole of why this cannot deadlock.</b> Two callers wanting the same two
    /// resources in opposite orders is the textbook way to hang an app, and ordering the keys globally
    /// means there is no opposite order to want them in. A cancellation part way through releases what
    /// was already taken, which is what <c>acquired</c> is for.
    /// </remarks>
    private async Task<IResourceLease> AcquireAsync(
        IEnumerable<string> keys,
        bool exclusive,
        string holder,
        CancellationToken cancellationToken)
    {
        var ordered = Order(keys);

        if (ordered.Count == 0)
        {
            return Empty(holder);
        }

        var acquired = new List<string>(ordered.Count);

        try
        {
            foreach (var key in ordered)
            {
                await AcquireOneAsync(key, exclusive, holder, cancellationToken);

                acquired.Add(key);
            }
        }
        catch
        {
            foreach (var key in acquired)
            {
                Release(key, exclusive, holder);
            }

            throw;
        }

        Raise();

        return Composite(ordered, exclusive, holder);
    }

    private Task AcquireOneAsync(string key, bool exclusive, string holder, CancellationToken cancellationToken)
    {
        Waiter waiter;

        lock (_gate)
        {
            var resource = Resolve(key);

            if (CanEnterNow(resource, exclusive))
            {
                Enter(resource, exclusive, holder);

                return Task.CompletedTask;
            }

            // Continuations run asynchronously so that granting a lease from inside the lock cannot
            // run the next caller's work - and therefore its next acquisition - on the releasing
            // thread while that lock is still held.
            waiter = new Waiter(
                exclusive,
                holder,
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

            waiter.Node = resource.Waiters.AddLast(waiter);
        }

        return WaitAsync(waiter, cancellationToken);
    }

    /// <summary>
    /// Waits to be granted, and unqueues itself if the caller gives up first.
    /// </summary>
    /// <remarks>
    /// The registration is taken after the waiter is queued and disposed before returning, so a token
    /// that fires at the same moment as the grant finds the completion already settled and
    /// <see cref="TaskCompletionSource.TrySetCanceled()"/> answers false - the lease is then held and
    /// the caller is the one that has it, rather than both or neither.
    /// </remarks>
    private async Task WaitAsync(Waiter waiter, CancellationToken cancellationToken)
    {
        await using var registration = cancellationToken.Register(() =>
        {
            lock (_gate)
            {
                if (waiter.Node is { List: not null } node)
                {
                    node.List.Remove(node);
                    waiter.Node = null;
                }
                else
                {
                    // Already granted, or already unqueued. Either way this is not ours to cancel.
                    return;
                }
            }

            waiter.Completion.TrySetCanceled(cancellationToken);
        });

        await waiter.Completion.Task;
    }

    private void Release(string key, bool exclusive, string holder)
    {
        List<TaskCompletionSource> granted = [];

        lock (_gate)
        {
            if (_resources.TryGetValue(key, out var resource) is false)
            {
                return;
            }

            if (exclusive)
            {
                resource.Exclusive = false;
            }
            else
            {
                resource.Shared = Math.Max(0, resource.Shared - 1);
            }

            resource.Holders.Remove(holder);

            Pump(resource, granted);

            // Nothing held and nobody waiting: the entry would otherwise be a name this dictionary
            // remembered forever, one per mod folder and store the app has ever touched.
            if (resource.IsHeld is false && resource.Waiters.Count == 0)
            {
                _resources.Remove(key);
            }
        }

        foreach (var completion in granted)
        {
            completion.TrySetResult();
        }

        Raise();
    }

    /// <summary>
    /// Grants from the head of the queue for as long as the head can be granted.
    /// </summary>
    /// <remarks>
    /// <b>Strictly first-come, which is what keeps both sides from starving.</b> Letting a newly
    /// arrived reader in past a waiting sweep would mean a busy disk never gets swept; letting the
    /// sweep in past readers already queued would stall applies behind housekeeping. Stopping at the
    /// first head that cannot enter gives neither preference.
    /// </remarks>
    private static void Pump(Resource resource, List<TaskCompletionSource> granted)
    {
        while (resource.Waiters.First is { Value: var waiter })
        {
            if (CanEnter(resource, waiter.IsExclusive) is false)
            {
                break;
            }

            resource.Waiters.RemoveFirst();
            waiter.Node = null;

            Enter(resource, waiter.IsExclusive, waiter.Holder);

            granted.Add(waiter.Completion);
        }
    }

    /// <summary>Whether the resource itself allows it, ignoring whoever is already queued.</summary>
    private static bool CanEnter(Resource resource, bool exclusive)
    {
        return exclusive
            ? resource.Exclusive is false && resource.Shared == 0
            : resource.Exclusive is false;
    }

    /// <summary>
    /// Whether a caller arriving now may walk straight in - which a queue makes a different question
    /// from <see cref="CanEnter"/>: a free resource with somebody waiting on it is not available to
    /// the next arrival, or the queue would never drain.
    /// </summary>
    private static bool CanEnterNow(Resource resource, bool exclusive)
    {
        return resource.Waiters.Count == 0 && CanEnter(resource, exclusive);
    }

    private static void Enter(Resource resource, bool exclusive, string holder)
    {
        if (exclusive)
        {
            resource.Exclusive = true;
        }
        else
        {
            resource.Shared++;
        }

        resource.Holders.Add(holder);
    }

    /// <summary>
    /// The keys of one claim, deduplicated and in a fixed order.
    /// </summary>
    /// <remarks>
    /// <b>Distinct matters as much as sorted.</b> Two of a game's mod targets can legitimately resolve
    /// to one key - a dedicated server and a client pointed at the same folder - and taking that key
    /// twice in one gesture would have the second take block on the first forever.
    /// </remarks>
    private static List<string> Order(IEnumerable<string> keys)
    {
        return [.. keys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary>The resource under <paramref name="key"/>, created if this is the first claim on it.</summary>
    private Resource Resolve(string key)
    {
        if (_resources.TryGetValue(key, out var resource) is false)
        {
            resource = new Resource();
            _resources[key] = resource;
        }

        return resource;
    }

    private IResourceLease Composite(IReadOnlyList<string> keys, bool exclusive, string holder)
    {
        return keys.Count == 1
            ? new Lease(this, keys[0], exclusive, holder)
            : new CompositeLease([.. keys.Select(x => new Lease(this, x, exclusive, holder))], exclusive);
    }

    private static IResourceLease Empty(string holder)
    {
        return new CompositeLease([], exclusive: false) { DisplayKey = holder };
    }

    private void Raise()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }


    /// <summary>One named resource: who is in it, and who is queued for it.</summary>
    private sealed class Resource
    {
        public int Shared { get; set; }
        public bool Exclusive { get; set; }

        /// <summary>
        /// Every current holder's sentence, in the order they arrived - so the first one is the one
        /// worth naming to somebody who has just been refused.
        /// </summary>
        public List<string> Holders { get; } = [];

        public LinkedList<Waiter> Waiters { get; } = new();

        public bool IsHeld => Exclusive || Shared > 0;
    }

    private sealed class Waiter(bool isExclusive, string holder, TaskCompletionSource completion)
    {
        public bool IsExclusive { get; } = isExclusive;
        public string Holder { get; } = holder;
        public TaskCompletionSource Completion { get; } = completion;

        /// <summary>
        /// Where this waiter sits, so giving up is a removal rather than a scan. Null once it has been
        /// granted or withdrawn, which is also how the two race each other safely.
        /// </summary>
        public LinkedListNode<Waiter>? Node { get; set; }
    }

    private sealed class Lease(ResourceLeases owner, string key, bool exclusive, string holder) : IResourceLease
    {
        private int _released;


        public string Key => key;
        public bool IsExclusive => exclusive;


        public void Dispose()
        {
            // Idempotent, for the same reason the background task strip's handle is: a using inside a
            // using, or a finally after an early return, must not release a claim twice and let two
            // callers into one mod folder.
            if (Interlocked.Exchange(ref _released, 1) == 1)
            {
                return;
            }

            owner.Release(key, exclusive, holder);
        }
    }

    /// <summary>Every key one gesture claimed, released together.</summary>
    private sealed class CompositeLease(IReadOnlyList<Lease> parts, bool exclusive) : IResourceLease
    {
        public string DisplayKey { get; init; } = "";

        public string Key => parts.Count == 0 ? DisplayKey : string.Join(", ", parts.Select(x => x.Key));
        public bool IsExclusive => exclusive;


        public void Dispose()
        {
            // Reverse, so the order things are given up in mirrors the order they were taken in. The
            // individual leases are idempotent, so a partially disposed composite is safe to dispose
            // again.
            for (var i = parts.Count - 1; i >= 0; i--)
            {
                parts[i].Dispose();
            }
        }
    }
}
