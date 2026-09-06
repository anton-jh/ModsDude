using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Core.Sync;

/// <summary>One instance as the monitor has to see it, which is three facts and no adapter.</summary>
/// <remarks>
/// An interface rather than <see cref="Services.LocalInstanceRepository"/> itself, for the same
/// reason <see cref="IInstanceModFolders"/> is one: the check runs off the persisted folder and the
/// persisted intent, so it works for an instance whose scope no repo on this machine serves, and it
/// can be exercised without a real <c>state.json</c>.
/// </remarks>
public interface IDriftCandidateSource
{
    IReadOnlyList<DriftCandidate> GetDriftCandidates();
}

public sealed record DriftCandidate(Guid InstanceId, string Name, string? ModFolder, ActiveProfile? ActiveProfile);

/// <summary>
/// Which revision a profile is on, for the profiles this client happens to know about.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately partial. The client holds the profile list of the repo it has loaded, so the answer
/// is there for the repo the user is standing in and absent for the rest - and absent is the honest
/// answer, because the alternative is a network round trip per instance on every window activation,
/// in a check whose entire point is that it works offline and costs a directory listing.
/// </para>
/// <para>
/// An interface for the same reason <see cref="IDriftCandidateSource"/> is one: the monitor depends
/// on the one fact it uses, and can be exercised without a signed-in client.
/// </para>
/// </remarks>
public interface IProfileRevisions
{
    /// <summary>The profile's current revision, or null where this client has not been told.</summary>
    int? GetHeadRevision(ActiveProfile profile);
}

/// <param name="ProfileName">
/// What the manifest recorded the profile was called. Null before an instance has ever synced, which
/// is also a state with no drift to report.
/// </param>
public sealed record InstanceDrift(DriftCandidate Instance, InstanceDriftReport Report, string? ProfileName)
{
    /// <summary>
    /// Whether this instance is worth telling somebody about.
    /// </summary>
    /// <remarks>
    /// A held savegame that has moved counts, even where the mod folder is exactly what was
    /// installed. <see cref="InstanceDriftStatus"/> is a statement about the mod folder and stays
    /// one; that the notice fires for two different kinds of problem is this line's business, not
    /// that enum's.
    /// </remarks>
    public bool IsDrifted => Report.Status is InstanceDriftStatus.Drifted || Report.HasSavegameDrift;
}

/// <param name="Reason">Only for the throttle: a check the user asked for is never dropped.</param>
public enum DriftCheckReason
{
    /// <summary>Startup, or the user asking. Always runs.</summary>
    Explicit,

    /// <summary>The window came forward. Throttled - it fires on every alt-tab.</summary>
    WindowActivated,

    /// <summary>A watcher saw the folder change. Throttled - an update-all fires it per file.</summary>
    FolderChanged
}


/// <summary>
/// The app-level answer to "do my mod folders still match their profiles", kept current across every
/// view.
/// </summary>
/// <remarks>
/// <para>
/// The manifest comparison is the primary mechanism, at startup and on window activation. It is the
/// only one that works in the normal case - ModsDude closed while the game runs - because the
/// manifest is frozen between syncs, so a comparison made later is still meaningful. A watcher
/// observing nothing can report nothing.
/// </para>
/// <para>
/// Activation checks are <b>throttled on the leading edge</b>: the first one runs immediately, and
/// alt-tabbing back and forth for the next few seconds does not buy another directory listing. The
/// leading edge rather than the trailing one because the point of checking on activation is that the
/// answer is on screen by the time the user has finished looking at the window.
/// </para>
/// <para>
/// Dismissal is deliberately weak: it lasts until the drift set changes or the app restarts. Nothing
/// here is persisted, and there is no "never show this again" - a dismissed warning that never comes
/// back is a savegame silently at risk.
/// </para>
/// </remarks>
public sealed class InstanceDriftMonitor : IDisposable
{
    /// <summary>
    /// Long enough that alt-tabbing between the game and ModsDude costs one listing rather than
    /// twenty, short enough that coming back from a play session always gets a fresh answer.
    /// </summary>
    public static readonly TimeSpan ThrottleWindow = TimeSpan.FromSeconds(5);

    private readonly IDriftCandidateSource _candidates;
    private readonly InstanceDriftService _driftService;
    private readonly SyncManifestStore _manifestStore;
    private readonly IProfileRevisions? _profileRevisions;
    private readonly ISavegameService? _savegames;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _lock = new();
    private readonly ILogger _logger;

    private readonly List<FileSystemWatcher> _watchers = [];

    private DateTimeOffset? _lastCheck;
    private string? _dismissedSignature;
    private IReadOnlyList<InstanceDrift> _results = [];


    /// <param name="savegames">
    /// Where the savegame half of the answer comes from. Optional, and absent for a build with no
    /// savegame support composed - the notice then says exactly what it has always said.
    /// </param>
    public InstanceDriftMonitor(
        IDriftCandidateSource candidates,
        InstanceDriftService driftService,
        SyncManifestStore manifestStore,
        IProfileRevisions? profileRevisions = null,
        TimeProvider? timeProvider = null,
        ISavegameService? savegames = null,
        ILogger<InstanceDriftMonitor>? logger = null)
    {
        _logger = logger ?? (ILogger)NullLogger.Instance;
        _candidates = candidates;
        _driftService = driftService;
        _manifestStore = manifestStore;
        _profileRevisions = profileRevisions;
        _savegames = savegames;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }


    /// <summary>Raised after any check that changed what the notice would say.</summary>
    public event EventHandler? Changed;


    /// <summary>Every instance that reported drift, most recently checked first.</summary>
    public IReadOnlyList<InstanceDrift> Drifted
    {
        get
        {
            lock (_lock)
            {
                return [.. _results.Where(x => x.IsDrifted)];
            }
        }
    }

    /// <summary>
    /// Whether the user waved the current drift away. Goes back to false the moment the drift set
    /// changes, because that is a different problem than the one they dismissed.
    /// </summary>
    public bool IsDismissed
    {
        get
        {
            lock (_lock)
            {
                return _dismissedSignature is not null && _dismissedSignature == Signature(_results);
            }
        }
    }

    public bool HasDrift => Drifted.Count > 0;

    public bool ShouldNotify => IsDismissed is false && HasDrift;


    /// <summary>
    /// Runs the cheap check across every instance and reports whether the answer changed.
    /// </summary>
    /// <returns>False where the throttle swallowed the request, so nothing was looked at.</returns>
    public bool Check(DriftCheckReason reason = DriftCheckReason.Explicit)
        => CheckAsync(reason).GetAwaiter().GetResult();

    /// <summary>
    /// The same check off the calling thread, since it lists directories - and now hashes savegame
    /// slots - which may be slow.
    /// </summary>
    /// <remarks>
    /// <see cref="Task.Run(Func{Task{bool}})"/> rather than awaiting the core directly, so that the
    /// whole of it - including the synchronous directory listings before the first await - is off the
    /// caller's thread, and so that <see cref="Check"/> can block on it from a UI thread without the
    /// continuations queueing behind the block it is itself holding.
    /// </remarks>
    public Task<bool> CheckAsync(DriftCheckReason reason = DriftCheckReason.Explicit)
        => Task.Run(() => CheckCoreAsync(reason));


    private async Task<bool> CheckCoreAsync(DriftCheckReason reason)
    {
        lock (_lock)
        {
            if (ShouldRun(reason) is false)
            {
                return false;
            }

            _lastCheck = _timeProvider.GetUtcNow();
        }

        var results = new List<InstanceDrift>();

        foreach (var candidate in _candidates.GetDriftCandidates())
        {
            // Asked for every instance, including ones with no active profile: holding somebody's
            // evening in a slot is worth saying whether or not this folder has ever been synced.
            var savegameDrift = await CheckSavegamesAsync(candidate.InstanceId);

            if (candidate.ActiveProfile is not ActiveProfile active)
            {
                if (savegameDrift.Count > 0)
                {
                    results.Add(new InstanceDrift(
                        candidate,
                        InstanceDriftReport.For(InstanceDriftStatus.NoActiveProfile) with { SavegameDrift = savegameDrift },
                        null));
                }

                continue;
            }

            var report = _driftService.Check(
                candidate.InstanceId,
                active,
                candidate.ModFolder,
                currentRevision: _profileRevisions?.GetHeadRevision(active),
                savegameDrift: savegameDrift);

            // Only a drifted instance needs the manifest read a second time, and only to name the
            // profile. Everything else has nothing to say.
            var profileName = report.Status is InstanceDriftStatus.Drifted
                ? _manifestStore.TryRead(candidate.InstanceId)?.ProfileName
                : null;

            results.Add(new InstanceDrift(candidate, report, profileName));
        }

        bool changed;

        lock (_lock)
        {
            changed = Signature(_results) != Signature(results);
            _results = results;
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    /// <summary>
    /// The savegame half, or nothing where this build has none.
    /// </summary>
    /// <remarks>
    /// Its failures are swallowed on purpose. The mod half of the answer is the one that has always
    /// been there and it is computed already; losing all of it because a save folder went missing
    /// mid-check would trade a working notice for an exception on a background thread.
    /// </remarks>
    private async Task<IReadOnlyList<Savegames.SavegameDrift>> CheckSavegamesAsync(Guid instanceId)
    {
        if (_savegames is null)
        {
            return [];
        }

        try
        {
            return await _savegames.CheckDriftAsync(instanceId, CancellationToken.None);
        }
        catch (Exception exception)
        {
            // The notice degrades to the mod half rather than failing. Nothing on screen says the
            // savegame half was even attempted.
            _logger.LogWarning(exception, "Could not check savegame drift for instance {Instance}.", instanceId);

            return [];
        }
    }

    /// <summary>
    /// Silences the notice for the drift that is on screen right now, and nothing else. There is no
    /// permanent form of this on purpose.
    /// </summary>
    public void Dismiss()
    {
        lock (_lock)
        {
            _dismissedSignature = Signature(_results);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// A latency optimisation on top of the manifest comparison, for the narrower case where ModsDude
    /// happens to be open while the mods change. It decides nothing on its own - watchers miss events
    /// across sleep and on network paths, and the design must not depend on having been running.
    /// </summary>
    public void Watch()
    {
        StopWatching();

        foreach (var candidate in _candidates.GetDriftCandidates())
        {
            if (candidate.ModFolder is null || candidate.ActiveProfile is null)
            {
                continue;
            }

            try
            {
                var watcher = new FileSystemWatcher(candidate.ModFolder)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite
                };

                watcher.Changed += OnFolderChanged;
                watcher.Created += OnFolderChanged;
                watcher.Deleted += OnFolderChanged;
                watcher.Renamed += OnFolderChanged;

                // Its own failures are not the app's: a drive pulled out from under a watcher must
                // not surface as an unhandled exception on a background thread.
                watcher.Error += (_, _) => { };

                watcher.EnableRaisingEvents = true;

                _watchers.Add(watcher);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
            {
                // An unreachable folder is unknown, not drifted, and it is certainly not worth an
                // error dialog. The activation check reports it quietly when the time comes.
                _logger.LogWarning(exception, "Could not watch a mod folder for changes; drift there will only be noticed on a manual check.");
            }
        }
    }

    public void Dispose()
    {
        StopWatching();
    }


    private void StopWatching()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
    }

    private void OnFolderChanged(object sender, FileSystemEventArgs e)
    {
        // An update-all rewrites hundreds of files; the throttle is what keeps that from being
        // hundreds of directory listings.
        _ = CheckAsync(DriftCheckReason.FolderChanged);
    }

    private bool ShouldRun(DriftCheckReason reason)
    {
        if (reason is DriftCheckReason.Explicit || _lastCheck is not DateTimeOffset last)
        {
            return true;
        }

        return _timeProvider.GetUtcNow() - last >= ThrottleWindow;
    }

    /// <summary>
    /// What the notice would say, reduced to a string. Dismissal is against this rather than against
    /// a timestamp so that the same drift stays dismissed across re-checks while a new mod going
    /// wrong brings the notice straight back.
    /// </summary>
    private static string Signature(IReadOnlyList<InstanceDrift> results)
    {
        return string.Join(
            '|',
            results
                .Where(x => x.IsDrifted)
                .OrderBy(x => x.Instance.InstanceId)
                .Select(x => string.Join(
                    ';',
                    x.Instance.InstanceId,
                    x.Report.Status,
                    string.Join(',', x.Report.Added),
                    string.Join(',', x.Report.Removed),
                    string.Join(',', x.Report.Changed),
                    string.Join(',', x.Report.ProfileChangedMods.Select(m => m.Value)),
                    // So that a dismissed notice comes straight back when the profile moves again,
                    // which is a different problem than the one that was waved away.
                    x.Report.AppliedRevision,
                    x.Report.CurrentRevision,
                    // And when a savegame goes wrong under a dismissed mod warning: an evening that
                    // exists only on this disk is not covered by having waved away two stray mods.
                    string.Join(',', x.Report.SavegameDrift.Select(s => $"{s.SavegameId}:{s.Kind}")))));
    }
}
