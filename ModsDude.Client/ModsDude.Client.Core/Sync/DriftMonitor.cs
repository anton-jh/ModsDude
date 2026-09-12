using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.GameAdapters;
using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Core.Sync;

/// <summary>One game as the monitor has to see it, which is a few facts and no adapter.</summary>
/// <remarks>
/// An interface rather than <see cref="Services.GameRepository"/> itself, for the same
/// reason <see cref="IModFolders"/> is one: the check runs off the persisted folders and the
/// persisted intent, so it works for a game whose scope no repo on this machine serves, and it
/// can be exercised without a real <c>state.json</c>.
/// </remarks>
public interface IDriftCandidateSource
{
    IReadOnlyList<DriftCandidate> GetDriftCandidates();
}

/// <param name="Identity">Which game this is, and the key its holds are filed under.</param>
/// <param name="Targets">
/// Every folder it reaches, each carrying the key its own manifest is filed under. Read off the
/// persisted list, so it is answered for a game whose identity no loaded repo serves. Empty is an
/// ordinary answer - a game whose settings point at no folder - and the savegame half of the check
/// still has something to say about one.
/// </param>
public sealed record DriftCandidate(
    GameIdentity Identity,
    string Name,
    IReadOnlyList<GameModFolder> Targets,
    ActiveProfile? ActiveProfile);

/// <summary>
/// Which revision a profile is on, for the profiles this client happens to know about.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately partial. The client holds the profile list of the repo it has loaded, so the answer
/// is there for the repo the user is standing in and absent for the rest - and absent is the honest
/// answer, because the alternative is a network round trip per game on every window activation,
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

/// <summary>
/// One thing the notice could say: a game, whichever of its folders this is about, and what the
/// check found there.
/// </summary>
/// <remarks>
/// <b>One per target, because the mod half is per folder.</b> A game reaching three of them produces
/// three of these, and <em>which one did not get the apply</em> is the interesting half of a BeamMP
/// evening. The savegame half is the game's and is carried on every one of its entries, so whichever
/// entry the notice is showing says both things about the game the user is looking at rather than
/// racing a second notice to say the other.
/// </remarks>
/// <param name="Target">
/// Null where this is about the game rather than about one of its folders - it reaches none, or it
/// follows no profile, so there is no comparison to attribute to any particular one. Such an entry
/// exists for the savegame half alone, which is the answer this already gave before targets existed.
/// </param>
/// <param name="ProfileName">
/// What the manifest recorded the profile was called. Null before a folder has ever synced, which
/// is also a state with no drift to report.
/// </param>
public sealed record TargetDrift(
    DriftCandidate Game,
    GameModFolder? Target,
    DriftReport Report,
    string? ProfileName)
{
    /// <summary>
    /// Whether this is worth telling somebody about.
    /// </summary>
    /// <remarks>
    /// A held savegame that has moved counts, even where the mod folder is exactly what was
    /// installed. <see cref="DriftStatus"/> is a statement about the mod folder and stays
    /// one; that the notice fires for two different kinds of problem is this line's business, not
    /// that enum's.
    /// </remarks>
    public bool IsDrifted => Report.Status is DriftStatus.Drifted || Report.HasSavegameDrift;
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
public sealed class DriftMonitor : IDisposable
{
    /// <summary>
    /// Long enough that alt-tabbing between the game and ModsDude costs one listing rather than
    /// twenty, short enough that coming back from a play session always gets a fresh answer.
    /// </summary>
    public static readonly TimeSpan ThrottleWindow = TimeSpan.FromSeconds(5);

    private readonly IDriftCandidateSource _candidates;
    private readonly DriftService _driftService;
    private readonly SyncManifestStore _manifestStore;
    private readonly IProfileRevisions? _profileRevisions;
    private readonly IHeldSavegames? _savegames;
    private readonly StoreIntegrityService? _storeIntegrity;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _lock = new();
    private readonly ILogger _logger;

    private readonly List<FileSystemWatcher> _watchers = [];

    private DateTimeOffset? _lastCheck;
    private string? _dismissedSignature;
    private IReadOnlyList<TargetDrift> _results = [];
    private readonly List<CorruptedBlob> _corruption = [];


    /// <param name="savegames">
    /// Where the savegame half of the answer comes from. Optional, and absent for a build with no
    /// savegame support composed - the notice then says exactly what it has always said.
    /// </param>
    /// <param name="storeIntegrity">
    /// The rewritten-blob check, run against whatever the folder comparison found changed. Optional
    /// on the same terms as <paramref name="savegames"/>.
    /// </param>
    public DriftMonitor(
        IDriftCandidateSource candidates,
        DriftService driftService,
        SyncManifestStore manifestStore,
        IProfileRevisions? profileRevisions = null,
        TimeProvider? timeProvider = null,
        IHeldSavegames? savegames = null,
        StoreIntegrityService? storeIntegrity = null,
        ILogger<DriftMonitor>? logger = null)
    {
        _logger = logger ?? (ILogger)NullLogger.Instance;
        _candidates = candidates;
        _driftService = driftService;
        _manifestStore = manifestStore;
        _profileRevisions = profileRevisions;
        _savegames = savegames;
        _storeIntegrity = storeIntegrity;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }


    /// <summary>Raised after any check that changed what the notice would say.</summary>
    public event EventHandler? Changed;


    /// <summary>Every game that reported drift, most recently checked first.</summary>
    public IReadOnlyList<TargetDrift> Drifted
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
                return _dismissedSignature is not null && _dismissedSignature == Signature(_results, _corruption);
            }
        }
    }

    public bool HasDrift => Drifted.Count > 0;

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
    public IReadOnlyList<CorruptedBlob> StoreCorruption
    {
        get
        {
            lock (_lock)
            {
                return [.. _corruption];
            }
        }
    }

    public bool HasStoreCorruption => StoreCorruption.Count > 0;

    public bool ShouldNotify => IsDismissed is false && (HasDrift || HasStoreCorruption);


    /// <summary>
    /// Runs the cheap check across every game and reports whether the answer changed.
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

        var results = new List<TargetDrift>();

        foreach (var candidate in _candidates.GetDriftCandidates())
        {
            // Once per game rather than once per target, because the hold is the game's and the
            // check costs a hash of every held slot. Asked for every game, including ones with no
            // active profile: holding somebody's evening in a slot is worth saying whether or not
            // any of these folders has ever been synced.
            var savegameDrift = await CheckSavegamesAsync(candidate.Identity);

            if (candidate.ActiveProfile is not ActiveProfile active)
            {
                if (savegameDrift.Count > 0)
                {
                    // No folder named: with no profile there is no comparison to make against any of
                    // them, so this entry is about the game rather than about one of its folders.
                    results.Add(new TargetDrift(
                        candidate,
                        null,
                        DriftReport.For(DriftStatus.NoActiveProfile) with { SavegameDrift = savegameDrift },
                        null));
                }

                continue;
            }

            if (candidate.Targets.Count == 0)
            {
                // Nothing to compare, and one entry rather than none: the savegame half is still an
                // answer about this game, and a game whose settings point at no folder is exactly
                // where somebody wants to hear that a slot is still holding a save.
                results.Add(new TargetDrift(
                    candidate,
                    null,
                    DriftReport.For(DriftStatus.FolderUnreachable) with { SavegameDrift = savegameDrift },
                    null));

                continue;
            }

            foreach (var target in candidate.Targets)
            {
                var report = CheckMods(candidate, target, active, savegameDrift);

                // Runs off what the folder comparison just found changed, which is the only set of
                // files that can have been written to since the sync.
                report = report with
                {
                    StoreCorruption = await CheckStoreAsync(target, report.Changed)
                };

                // Only a drifted folder needs the manifest read a second time, and only to name the
                // profile. Everything else has nothing to say.
                var profileName = report.Status is DriftStatus.Drifted
                    ? _manifestStore.TryRead(target.Target)?.ProfileName
                    : null;

                results.Add(new TargetDrift(candidate, target, report, profileName));
            }
        }

        bool changed;

        lock (_lock)
        {
            var before = Signature(_results, _corruption);

            // Kept rather than replaced - see StoreCorruption. Deduplicated by address, since two
            // games served by one store can both reach a blob before it is gone.
            foreach (var blob in results.SelectMany(x => x.Report.StoreCorruption))
            {
                if (_corruption.Any(x => ModContentHasher.Matches(x.Hash, blob.Hash)) is false)
                {
                    _corruption.Add(blob);
                }
            }

            _results = results;

            changed = before != Signature(_results, _corruption);
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    /// <summary>
    /// The mod half: what one of this game's folders holds against what was last applied to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A backstop, not the fix.</b> The check answers
    /// <see cref="DriftStatus.FolderUnreachable"/> for a folder it cannot list, and a file that
    /// vanished between the listing and the read is drift rather than a throw - see
    /// <c>DriftService.HasMoved</c>, which is where that one escaped from. What this is for is
    /// the shape of the failure rather than any known instance of it: this loop runs unattended on
    /// every window activation, so one folder's disk must cost that folder's answer and not every
    /// other folder's with it, and nothing here may arrive as an unobserved task exception.
    /// </para>
    /// <para>
    /// <b>Narrower than the other two guards on purpose.</b> The savegame and store-integrity halves
    /// are additions to an answer that works without them, so they swallow anything; this one <em>is</em>
    /// the answer, and catching everything would turn a logic error in the drift service into a folder
    /// that reads as quietly unreachable forever. A disk is a disk and a bug is a bug.
    /// </para>
    /// </remarks>
    private DriftReport CheckMods(
        DriftCandidate candidate,
        GameModFolder target,
        ActiveProfile active,
        IReadOnlyList<Savegames.SavegameDrift> savegameDrift)
    {
        try
        {
            return _driftService.Check(
                target.Target,
                active,
                target.ModFolder,
                // A past savegame held here pins the folder to its own revision, and that is what
                // "up to date" means for this game until it is checked in. Nothing is suppressed
                // to achieve it: the comparison is against the number the game is supposed to be
                // on, and it comes out equal on its own. Against head instead, a game holding a
                // past savegame would report drift permanently and offer a re-apply to head that the
                // apply table refuses.
                currentRevision: _savegames?.GetRequiredRevision(candidate.Identity, active.ProfileId)
                    ?? _profileRevisions?.GetHeadRevision(active),
                savegameDrift: savegameDrift);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Could not check the mod folder {Folder} for drift.", target.ModFolder);

            // Unknown rather than drifted, and the savegame half is still carried: a held savegame is
            // worth saying whatever the folder turned out to be.
            return DriftReport.For(DriftStatus.FolderUnreachable) with { SavegameDrift = savegameDrift };
        }
    }

    /// <summary>
    /// The savegame half, or nothing where this build has none.
    /// </summary>
    /// <remarks>
    /// Its failures are swallowed on purpose. The mod half of the answer is the one that has always
    /// been there and it is computed already; losing all of it because a save folder went missing
    /// mid-check would trade a working notice for an exception on a background thread.
    /// </remarks>
    private async Task<IReadOnlyList<Savegames.SavegameDrift>> CheckSavegamesAsync(GameIdentity game)
    {
        if (_savegames is null)
        {
            return [];
        }

        try
        {
            return await _savegames.CheckDriftAsync(game, CancellationToken.None);
        }
        catch (Exception exception)
        {
            // The notice degrades to the mod half rather than failing. Nothing on screen says the
            // savegame half was even attempted.
            _logger.LogWarning(exception, "Could not check savegame drift for game {Game}.", game);

            return [];
        }
    }

    /// <summary>
    /// The rewritten-blob half, or nothing where this build has none.
    /// </summary>
    /// <remarks>
    /// Swallows its failures for the same reason <see cref="CheckSavegamesAsync"/> does, and it has
    /// one more of its own to swallow: a mod folder on a drive that went away between the listing
    /// and the identity read. Nothing found means nothing said, which is also the honest answer for
    /// a filesystem that cannot report file identities at all.
    /// </remarks>
    private async Task<IReadOnlyList<CorruptedBlob>> CheckStoreAsync(GameModFolder target, IReadOnlyList<string> changed)
    {
        if (_storeIntegrity is null || changed.Count == 0)
        {
            return [];
        }

        try
        {
            return await _storeIntegrity.CheckAsync(
                target.Target,
                target.ModFolder,
                changed,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not check store integrity for target {Target}.", target.Target);

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
            _dismissedSignature = Signature(_results, _corruption);
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

        // One watcher per folder, so a game whose server folder is being updated while its client
        // folder sits still hears about the one that moved.
        foreach (var target in _candidates.GetDriftCandidates()
            .Where(x => x.ActiveProfile is not null)
            .SelectMany(x => x.Targets))
        {
            try
            {
                var watcher = new FileSystemWatcher(target.ModFolder)
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
    /// <param name="corruption">
    /// The <em>accumulated</em> set, never the per-check one. A corrupt blob is deleted as it is
    /// found, so the reports empty out on the next pass; signing against them would un-dismiss the
    /// notice every time a finding aged out, which is the exact opposite of what dismissal means.
    /// </param>
    private static string Signature(IReadOnlyList<TargetDrift> results, IReadOnlyList<CorruptedBlob> corruption)
    {
        return string.Join(
            "//",
            SignatureOfDrift(results),
            // So that a second blob going wrong under a dismissed notice brings it straight back.
            // This is the one thing here that outlives the check that found it.
            string.Join(',', corruption.Select(x => x.Hash).Order(StringComparer.OrdinalIgnoreCase)));
    }

    private static string SignatureOfDrift(IReadOnlyList<TargetDrift> results)
    {
        return string.Join(
            '|',
            results
                .Where(x => x.IsDrifted)
                // A ModTargetRef is not comparable, and the signature only needs a stable order. By
                // target rather than by game, so that the server folder going wrong under a notice
                // dismissed about the client folder brings it straight back.
                .OrderBy(x => x.Target?.Target.ToString() ?? x.Game.Identity.ToString(), StringComparer.Ordinal)
                .Select(x => string.Join(
                    ';',
                    x.Game.Identity,
                    x.Target?.Target.Key,
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
