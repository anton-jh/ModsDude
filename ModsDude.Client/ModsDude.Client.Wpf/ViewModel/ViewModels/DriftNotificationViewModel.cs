using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.ViewModel.Services;
using System.Collections.Specialized;
using System.Windows;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// The app-level drift notice: installed mods no longer match the applied profile, said from every
/// view and not owned by any page.
/// </summary>
/// <remarks>
/// <para>
/// It lives in the shell beside the modal slot, and it is deliberately <b>not</b> a modal. The user
/// launched the game themselves, updated mods from inside it and came back; what matters is what
/// they see on returning, and blocking the app on the way in would be a worse answer than the
/// problem.
/// </para>
/// <para>
/// Two actions. Review opens the drifted profile's mod list, which is also where the versions the
/// game just downloaded get imported, and it leads: what the folder now holds is usually what the
/// user meant to end up with, and re-applying without looking throws it away. Re-apply is the second
/// one, in one click, for when they only want their locked versions back.
/// </para>
/// <para>
/// A guest gets one action. They cannot import, so the page Review would open is the read-only mod
/// list - which is not an answer to "your mod folder has drifted" - and re-applying is the whole of
/// what is theirs to do. See <see cref="CanReview"/>.
/// </para>
/// <para>
/// Dismissal lasts until the drift set changes or the app restarts. There is no permanent form of it:
/// a dismissed warning that never comes back is a savegame silently at risk. See
/// docs/07-mod-sync-design.md#it-has-to-be-unmissable-everywhere.
/// </para>
/// <para>
/// <b>Two rules where a past savegame is held, both about not crying wolf.</b> It never says "behind
/// the profile" - nothing here suppresses that, the drift check is simply given the revision the
/// game is supposed to be on and the comparison comes out equal. And folder drift still reports,
/// but its action reads <em>Re-apply rev 4</em> rather than offering a latest the apply table refuses.
/// See docs/10-savegame-profile-binding.md#drift-notice.
/// </para>
/// </remarks>
public partial class DriftNotificationViewModel : ObservableObject, IDisposable
{
    private readonly DriftMonitor _monitor;
    private readonly RepoRepository _repoRepository;
    private readonly GameRepository _instanceRepository;
    private readonly ProfileService _profileService;
    private readonly SavegameBindingStore _bindingStore;
    private readonly IHeldSavegames _heldSavegames;
    private readonly ProfileApplyService _applyService;
    private readonly ShellNavigationService _navigation;

    /// <summary>The one place the notice is suppressed: the drifted profile's own mod list editor.</summary>
    private readonly HashSet<ActiveProfile> _suppressed = [];

    private TargetDrift? _subject;


    public DriftNotificationViewModel(
        DriftMonitor monitor,
        RepoRepository repoRepository,
        GameRepository instanceRepository,
        ProfileService profileService,
        SavegameBindingStore bindingStore,
        IHeldSavegames heldSavegames,
        ProfileApplyService applyService,
        ShellNavigationService navigation)
    {
        _monitor = monitor;
        _repoRepository = repoRepository;
        _instanceRepository = instanceRepository;
        _profileService = profileService;
        _bindingStore = bindingStore;
        _heldSavegames = heldSavegames;
        _applyService = applyService;
        _navigation = navigation;

        _monitor.Changed += OnDriftChanged;
        _instanceRepository.Games.CollectionChanged += OnGamesChanged;

        // Drift is detected from the manifest and the folder, so this notice can be up before the
        // repo list has been fetched - it is raised from the window's constructor, and the repos are
        // loaded by a command on the shell underneath it. Everything the notice says about
        // membership is unknowable until they land, and nothing else would re-ask.
        _repoRepository.Repos.CollectionChanged += OnReposChanged;

        // Everything else that can change the answer, wired here rather than remembered at each call
        // site. A user who edits a profile, repoints a game or checks a save out and then tabs
        // back to the game must not be the first to find out that they are out of sync - so the check
        // is driven by the facts changing, not by anybody remembering to ask.
        _instanceRepository.GameChanged += OnFactsChanged;
        _profileService.ProfileUpdated += OnProfileUpdated;
        _bindingStore.BindingsChanged += OnFactsChanged;
    }


    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private string _headline = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    private string _detail = "";

    /// <summary>
    /// Whether the mod half found anything to say. It can be empty - a game reported only for
    /// the savegame it is holding has no file counts and no moved revision - and an empty line under
    /// the headline reads as something that failed to load.
    /// </summary>
    public bool HasDetail => string.IsNullOrWhiteSpace(Detail) is false;

    /// <summary>
    /// Named separately from the count on purpose. An unlocked mod at the wrong version is untidy; a
    /// locked map at the wrong version is a damaged savegame waiting to happen.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLockedWarning))]
    private string? _lockedWarning;

    /// <summary>
    /// The savegame half, said out loud rather than folded into the count.
    /// </summary>
    /// <remarks>
    /// <b>This is the half the notice used to compute and never print.</b> A held savegame that has
    /// moved makes a game drifted on its own - see <c>TargetDrift.IsDrifted</c> - so the notice
    /// could be raised entirely by it, and the detail line, which only ever described mod files and
    /// revisions, then had nothing to say. An empty mod folder on an empty profile is exactly that
    /// case, and it read as a warning with no reason in it.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSavegameWarning))]
    private string? _savegameWarning;

    /// <summary>
    /// A store blob the game rewrote in place through a hardlink, caught and dropped.
    /// </summary>
    /// <remarks>
    /// Read off the monitor's accumulated set rather than off this game's report, and
    /// deliberately: a corrupt blob is deleted the moment it is found, so the report that carried it
    /// is empty by the next check. It is also the only line here that is not about one game - a
    /// store is shared by every repo on its volume - which is why it names the drive.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStoreWarning))]
    private string? _storeWarning;

    /// <summary>
    /// The drifted files are by definition versions the user now has and the repo may not, so the
    /// warning doubles as the first step of the flow they came back to perform anyway.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImportPrompt))]
    private string? _importPrompt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _status;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReapplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenModListCommand))]
    private bool _isBusy;

    /// <summary>
    /// Whether reviewing is a thing this user can do at all. A guest cannot import, and the mod list
    /// they would land on is the read-only one - so the button is not offered rather than offered and
    /// refused, and the prompt that sends them there is not written either. Re-applying is theirs,
    /// which is why the notice still has something to do.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenModListCommand))]
    [NotifyPropertyChangedFor(nameof(ReapplyIsPrimary))]
    [NotifyPropertyChangedFor(nameof(CanReapplyBesideReview))]
    private bool _canReview;

    /// <summary>
    /// Whether re-applying is the notice's leading action. It is not, wherever Review is offered -
    /// looking first is the better move, and re-applying is what discards what the game downloaded.
    /// For a guest there is nothing else to offer, so it leads by being the only one.
    /// </summary>
    /// <summary>
    /// Whether there is anything to re-apply. False for a game reported only because it is
    /// holding a savegame and that has never been pointed at a profile - there is no mod list to put
    /// its folder back onto, and the buttons say so by not being there.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReapplyCommand))]
    [NotifyPropertyChangedFor(nameof(ReapplyIsPrimary))]
    [NotifyPropertyChangedFor(nameof(CanReapplyBesideReview))]
    private bool _canReapply = true;

    /// <summary>
    /// What the re-apply button says.
    /// </summary>
    /// <remarks>
    /// <b>Never "apply latest" for a game holding a past savegame.</b> Head is exactly what the apply
    /// table refuses there, so a button offering it would be one that fails when pressed - and the
    /// revision it does target is a number worth seeing before pressing anything, since the folder is
    /// deliberately behind head and staying there.
    /// </remarks>
    [ObservableProperty]
    private string _reapplyLabel = "Re-apply now";

    public bool ReapplyIsPrimary => CanReapply && CanReview is false;

    /// <summary>The plain Re-apply, which is only drawn where Review is leading beside it.</summary>
    public bool CanReapplyBesideReview => CanReapply && CanReview;

    public bool HasLockedWarning => LockedWarning is not null;
    public bool HasSavegameWarning => SavegameWarning is not null;
    public bool HasStoreWarning => StoreWarning is not null;
    public bool HasImportPrompt => ImportPrompt is not null;
    public bool HasStatus => Status is not null;


    /// <summary>The first check plus the watcher, once the shell is up.</summary>
    public void Start()
    {
        _ = _monitor.CheckAsync();

        _monitor.Watch();
    }

    /// <summary>
    /// Window activation. Throttled inside the monitor, since this fires on every alt-tab and someone
    /// switching back and forth does not need a directory listing each time.
    /// </summary>
    public void NotifyWindowActivated()
    {
        _ = _monitor.CheckAsync(DriftCheckReason.WindowActivated);
    }

    /// <summary>
    /// Hides the notice while the user is looking at the very thing it would tell them about. Paired
    /// with <see cref="Release"/> when that editor closes - never persisted, and never widened to a
    /// second surface.
    /// </summary>
    public void SuppressFor(ActiveProfile profile)
    {
        _suppressed.Add(profile);

        Refresh();
    }

    /// <summary>
    /// Stops suppressing, and re-checks rather than only redrawing.
    /// </summary>
    /// <remarks>
    /// The editor is the one page that can change what this notice would say while it is being told
    /// not to say it - removing a mod and saving without applying is exactly that - so the last
    /// computed answer is the one thing that must not be trusted at the moment the suppression lifts.
    /// </remarks>
    public void Release(ActiveProfile profile)
    {
        _suppressed.Remove(profile);

        Refresh();

        _ = _monitor.CheckAsync();
    }

    public void Dispose()
    {
        _monitor.Changed -= OnDriftChanged;
        _instanceRepository.Games.CollectionChanged -= OnGamesChanged;
        _repoRepository.Repos.CollectionChanged -= OnReposChanged;
        _instanceRepository.GameChanged -= OnFactsChanged;
        _profileService.ProfileUpdated -= OnProfileUpdated;
        _bindingStore.BindingsChanged -= OnFactsChanged;
    }


    [RelayCommand(CanExecute = nameof(CanReviewNow))]
    private async Task OpenModList()
    {
        if (_subject?.Game.ActiveProfile is not ActiveProfile active)
        {
            return;
        }

        if (_subject.Target is not GameModFolder target)
        {
            // Nothing to scan: this entry is here for a held savegame and names no folder.
            Status = "This game reaches no mod folder, so there is nothing on disk to look at.";

            return;
        }

        if (await _navigation.GoToProfileModsAsync(active.RepoId, active.ProfileId, target.Target) is false)
        {
            Status = "That profile could not be opened from here - pick it in the sidebar.";
        }
    }

    /// <summary>
    /// One click, because most of the time there is nothing to change and the user just wants their
    /// locked versions back. Applies to every game on the drifted profile, which is the derived
    /// target set and not a choice.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAct), IncludeCancelCommand = true)]
    private async Task Reapply(CancellationToken cancellationToken)
    {
        if (_subject?.Game.ActiveProfile is not ActiveProfile active)
        {
            return;
        }

        if (FindRepo(active.RepoId) is not Repo repo)
        {
            // The same window the Review button is missing in: this notice can be up before the repo
            // list has arrived. Saying so beats a button that does nothing when pressed.
            Status = "The repo this game follows has not loaded yet. Try again in a moment.";

            return;
        }

        IsBusy = true;
        Status = "Re-applying...";

        try
        {
            // The game this notice is about, not every game on the profile: the entry names one, and
            // a game is configured once, so there is nothing else the profile could reach. It does
            // reach every folder that game has, which is the apply's own loop. Pure apply - the game
            // already follows this profile, which is why it is drifted from it.
            Status = _instanceRepository.Find(_subject.Game.Identity) is Game game
                ? (await _applyService.ApplyAsync(
                    repo,
                    game,
                    active.ProfileId,
                    _subject.ProfileName,
                    confirmPlan: false,
                    progress: null,
                    cancellationToken)).Message
                : null;
        }
        finally
        {
            IsBusy = false;
        }

        await _monitor.CheckAsync();
    }

    private bool CanAct() => IsBusy is false && CanReapply;

    private bool CanReviewNow() => IsBusy is false && CanReview;

    [RelayCommand]
    private void Dismiss()
    {
        _monitor.Dismiss();
    }


    private void OnDriftChanged(object? sender, EventArgs e)
    {
        // The monitor runs its checks off the UI thread, and everything below is bound.
        _ = Application.Current?.Dispatcher.InvokeAsync(Refresh);
    }

    /// <summary>
    /// The repo list arriving, or being swapped for another account's. No drift check is needed - the
    /// drift has not changed, only what is known about who the user is in the repo it belongs to.
    /// </summary>
    private void OnReposChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _ = Application.Current?.Dispatcher.InvokeAsync(Refresh);
    }

    private void OnGamesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // A folder that just arrived is not being watched yet, and one that left is being watched for
        // nothing.
        _monitor.Watch();

        _ = _monitor.CheckAsync();
    }

    /// <summary>
    /// Something the check reads has changed: a game repointed, a profile that moved on, a
    /// savegame taken or handed back.
    /// </summary>
    /// <remarks>
    /// <see cref="DriftCheckReason.Explicit"/> by default, so the throttle never swallows one of
    /// these - they are consequences of something the user just did, and the whole complaint these
    /// answer is a notice that arrives one alt-tab too late.
    /// </remarks>
    private void OnFactsChanged(object? sender, EventArgs e)
    {
        // Re-watched as well as re-checked: a game whose mod folder moved is being watched at
        // the old path.
        _monitor.Watch();

        _ = _monitor.CheckAsync();
    }

    private void OnProfileUpdated(Guid profileId)
    {
        // A profile's head revision moving is drift for every folder built against the old one,
        // whether this client saved it or a teammate did and a refresh brought it back.
        _ = _monitor.CheckAsync();
    }

    private void Refresh()
    {
        var drifted = _monitor.Drifted
            .Where(x => x.Game.ActiveProfile is not ActiveProfile active || _suppressed.Contains(active) is false)
            .ToList();

        _subject = drifted.FirstOrDefault();

        if (_monitor.IsDismissed || _subject is null)
        {
            IsVisible = false;
            Status = null;

            return;
        }

        var report = _subject.Report;
        var profile = _subject.ProfileName is string name ? $"'{name}'" : "the applied profile";
        var files = report.Added.Count + report.Removed.Count + report.Changed.Count;

        // Across every entry of the game being shown, because a held save belongs to the folder it
        // sits in and the entry on top is whichever folder came first. One notice says both halves
        // about one game; two notices racing to say one each is how a warning becomes noise.
        var savegames = drifted
            .Where(x => x.Game.Identity == _subject.Game.Identity)
            .SelectMany(x => x.Report.SavegameDrift)
            .ToList();

        Headline = DescribeHeadline(
            drifted.DistinctBy(x => x.Game.Identity).Count(),
            _subject.Game.Name,
            profile,
            report,
            savegames.Count > 0);

        // Re-applying needs somewhere to apply *to*. A game reported purely because it is holding
        // a savegame may have no active profile at all, and an accent button that returns the moment
        // it is pressed is worse than no button.
        CanReapply = _subject.Game.ActiveProfile is not null;

        CanReview = _subject.Game.ActiveProfile is ActiveProfile active
            && FindRepo(active.RepoId) is Repo repo
            && repo.MembershipLevel >= RepoMembershipLevel.Member;

        ReapplyLabel = DescribeReapply(_subject.Game);

        Detail = Describe(_subject, report, files);
        LockedWarning = DescribeLocked(report);
        SavegameWarning = DescribeSavegames(savegames);
        StoreWarning = DescribeStoreCorruption(_monitor.StoreCorruption);
        ImportPrompt = files > 0 && CanReview
            ? "The versions now on disk may not be in the repo. Opening the mod list is where they get imported."
            : null;

        IsVisible = true;

        ReapplyCommand.NotifyCanExecuteChanged();
        OpenModListCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Which revision the one-click re-apply is going to install, where that is not simply the
    /// profile's latest.
    /// </summary>
    /// <remarks>
    /// Asked of <see cref="IHeldSavegames.GetRequiredRevision"/>, which is the same rule
    /// <see cref="ModsDude.Client.Core.Sync.ModSyncService"/> resolves the apply against - so the
    /// number on the button is the number that gets installed rather than a second guess at it.
    /// </remarks>
    private string DescribeReapply(DriftCandidate game)
    {
        if (game.ActiveProfile is not ActiveProfile active)
        {
            return "Re-apply now";
        }

        return _heldSavegames.GetRequiredRevision(game.Identity, active.ProfileId) is int revision
            ? $"Re-apply rev {revision}"
            : "Re-apply now";
    }

    /// <summary>
    /// What the notice is about, in one line.
    /// </summary>
    /// <remarks>
    /// Three sentences rather than one, because a game can be here for two unrelated reasons and
    /// the notice used to name only the first. A folder that matches its profile exactly and is
    /// holding somebody's evening in a slot was announced as "no longer matches the applied profile",
    /// followed by no detail at all - which is how a mod folder and a profile that are both empty
    /// produced a warning with nothing in it.
    /// </remarks>
    /// <param name="count">
    /// How many <em>games</em> are drifted, not how many entries: a game reaching three folders
    /// contributes one line's worth of news however many of them went out of step, and "3 games have
    /// drifted" for one game's three folders would be a plain lie. Slice 5 of Phase 10 is where the
    /// wording gets to name the folder.
    /// </param>
    /// <param name="hasSavegameDrift">
    /// Whether the <em>game</em> is holding anything that has drifted, gathered across its folders -
    /// not whether the entry being shown carries it, which with several targets is a different
    /// question.
    /// </param>
    private static string DescribeHeadline(int count, string instanceName, string profile, DriftReport report, bool hasSavegameDrift)
    {
        if (count > 1)
        {
            return $"{count} games have drifted";
        }

        // Named for the consequence rather than for the condition, which is why an activation that
        // did not land says where the folder still is rather than what failed.
        var mods = report.Status switch
        {
            DriftStatus.Drifted => $"'{instanceName}' no longer matches {profile}",

            DriftStatus.NotApplied => report.AppliedProfileName is string applied
                ? $"'{instanceName}' is still on '{applied}'"
                : $"'{instanceName}' is still on the mod list it was last applied to",

            DriftStatus.FolderRepointed => $"'{instanceName}' has a mod folder nothing has been applied to",

            // Deliberately not naming the profile: the name the notice has is the one the manifest
            // recorded, and the whole of this status is that there is no manifest.
            DriftStatus.NeverSynced => $"Nothing has been applied to '{instanceName}'",

            // Every remaining status is one the notice does not fire for on its own, so reaching
            // here means the savegame half is why this is on screen at all.
            _ => null
        };

        if (mods is null)
        {
            return $"'{instanceName}' is holding a savegame that no longer agrees with the repo";
        }

        return hasSavegameDrift ? $"{mods}, and its savegame has moved too" : mods;
    }

    /// <param name="subject">
    /// The entry being shown, so the two statuses that are about a folder rather than about its
    /// contents can say <em>which</em> folder - with several targets, which one did not get the apply
    /// is the interesting half. Named by key and only where the game reaches more than one, the same
    /// rule every other folder name in the app follows.
    /// </param>
    private static string Describe(TargetDrift subject, DriftReport report, int files)
    {
        if (report.Status is DriftStatus.NeverSynced)
        {
            return $"This game follows a profile and there is no record of it ever being applied{In(subject)} - " +
                   "either it never was, or the record was lost. Applying it is what makes the two agree, " +
                   "and until then nothing here can tell you whether the mods are right.";
        }

        if (report.Status is DriftStatus.NotApplied)
        {
            return $"Nothing was installed or removed{In(subject)}: it is exactly as its last apply left it. " +
                   "Re-applying is what moves it - nothing here needs repairing first.";
        }

        if (report.Status is DriftStatus.FolderRepointed)
        {
            return $"The settings now point{In(subject)} at {subject.Target?.ModFolder ?? "another folder"}, " +
                   "which nothing has been applied to. Re-applying is what fills it in.";
        }

        var parts = new List<string>();

        if (report.Changed.Count > 0) parts.Add($"{report.Changed.Count} replaced");
        if (report.Added.Count > 0) parts.Add($"{report.Added.Count} added");
        if (report.Removed.Count > 0) parts.Add($"{report.Removed.Count} removed");

        var folder = files > 0
            ? $"{string.Join(", ", parts)} in the mod folder since it was last applied. Updating mods from inside the game looks like this."
            : "";

        var pins = report.ProfileChangedMods.Count > 0
            ? $"{report.ProfileChangedMods.Count} mods are pinned differently than what is installed - somebody has edited the profile since."
            : "";

        return string.Join(' ', new[] { folder, DescribeRevision(report), pins }.Where(x => x.Length > 0));
    }

    /// <summary>
    /// " in the 'MP client' folder", or nothing at all for a game with one - which is nearly every
    /// game, and which is why these sentences read exactly as they did before targets existed.
    /// </summary>
    /// <remarks>
    /// <see cref="TargetNames"/>' answer, which is the same one every other folder name in the app
    /// gets. The adapter's own name for the folder is available here without an adapter because it
    /// is written down beside the path - this notice is up before the repo list has loaded, and that
    /// is precisely why it is persisted; a list written before names were recorded falls back to the
    /// key.
    /// </remarks>
    private static string In(TargetDrift subject)
        => Folder(subject) is string folder ? $" in the '{folder}' folder" : "";

    /// <summary>
    /// Which of the game's folders this entry is about, or null where there is nothing to tell apart
    /// - one folder, or an entry that is about the game rather than a folder of it.
    /// </summary>
    private static string? Folder(TargetDrift subject)
        => subject.Target?.NameAmong(subject.Game.Targets.Count);

    /// <summary>
    /// The half of drift no directory listing can find: the folder is exactly what was installed,
    /// and what was installed is no longer what the profile says. Two numbers, because that is all
    /// the cheap check has - and two numbers is enough to say something specific.
    /// </summary>
    private static string DescribeRevision(DriftReport report)
    {
        if (report.ProfileHasMoved is false)
        {
            return "";
        }

        return $"This folder was made to match revision {report.AppliedRevision}; the profile is now at revision {report.CurrentRevision}.";
    }

    /// <summary>
    /// The dangerous case, with the consequence named rather than folded into a count.
    /// </summary>
    private static string? DescribeLocked(DriftReport report)
    {
        if (report.LockedDrift.Count == 0)
        {
            return null;
        }

        var first = report.LockedDrift[0];
        var version = first.AppliedVersion is string applied ? $" at {applied}" : "";

        var what = first.Reason switch
        {
            LockedDriftReason.FileRemoved => $"'{first.DisplayName}' is locked{version} and is no longer in the mod folder.",
            LockedDriftReason.ProfileMoved => $"'{first.DisplayName}' is locked and the profile no longer pins the version installed here{version}.",
            _ => $"'{first.DisplayName}' is locked{version} and its file has changed since it was applied."
        };

        var more = report.LockedDrift.Count > 1
            ? $" {report.LockedDrift.Count - 1} more locked mods are affected as well."
            : "";

        return $"{what} Hosting a savegame on it may damage that save.{more}";
    }

    /// <summary>
    /// The one warning here that is not about this game. Named for the blast radius rather than
    /// the mod, because the mod is the symptom and the shared cache is the problem.
    /// </summary>
    private static string? DescribeStoreCorruption(IReadOnlyList<CorruptedBlob> corruption)
    {
        if (corruption.Count == 0)
        {
            return null;
        }

        var first = corruption[0];

        var outcome = first.Removed
            ? "The bad copy has been dropped and will be downloaded again when something needs it."
            : "It could not be dropped - something still has the file open - and will be caught again on the next check.";

        var more = corruption.Count > 1
            ? $" {corruption.Count - 1} more were found the same way."
            : "";

        return $"The game wrote '{first.DisplayName}' directly into the mod cache on {first.VolumeRoot}, " +
            $"which every repo on that drive shares. {outcome}{more}";
    }

    /// <summary>
    /// What the savegame check found, named for its consequence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One sentence for the first, and a count for the rest - the same shape as the locked-mod
    /// warning above it, and for the same reason: a person acts on one problem at a time, and a list
    /// of three would be read as a wall rather than as three things.
    /// </para>
    /// <para>
    /// The kinds are not exclusive, and the order they are reported in is
    /// <see cref="SavegameDriftRules"/>' - which puts unchecked-in play first. That is the right one
    /// to lead with: it is the state where somebody's evening exists on this disk and nowhere else.
    /// </para>
    /// </remarks>
    /// <param name="drift">
    /// Everything the game is holding that has drifted, gathered across its folders rather than read
    /// off the one entry being shown. A held save belongs to the target it sits in, so the entry the
    /// notice happens to be showing may carry none of them - and a notice about a game that said
    /// nothing about its savegames because the drifted one is in another folder is exactly the
    /// silence this phase exists to remove.
    /// </param>
    private static string? DescribeSavegames(IReadOnlyList<SavegameDrift> drift)
    {
        if (drift.Count == 0)
        {
            return null;
        }

        var first = drift[0];
        var save = first.SlotDisplayName is { Length: > 0 } named ? $"'{named}'" : "A savegame checked out here";

        var what = first.Kind switch
        {
            SavegameDriftKind.UncheckedInPlay =>
                $"{save} has been played since it was checked out, and that play exists nowhere but this disk until it is checked in.",

            SavegameDriftKind.TakenOverAndCheckedIn =>
                $"{save} has been checked in by somebody else - they are on version {first.HeadVersion}, this machine is holding version "
                    + $"{first.HeldVersion}. Checking in from here forks it, and will be refused unless you force it.",

            // Two sentences for one kind, because the rule reaches it two ways and only one of them
            // is about numbers. Against another profile entirely, putting the two revisions side by
            // side would be comparing integers belonging to different mod lists - which is the thing
            // the design refuses, said by the notice meant to explain it.
            SavegameDriftKind.PlayedOnAnotherModList when first.RunsOnAnotherProfile =>
                $"{save} follows a different mod list from the one this mod folder is on. Playing it on the wrong mod list is what damages a save.",

            // The savegame's own target, never the revision it was checked out against: those differ
            // on the ordinary follow-the-profile flow, and the target is what the comparison used.
            SavegameDriftKind.PlayedOnAnotherModList =>
                $"{save} runs on revision {first.TargetRevision} of its mod list and this mod folder is on revision "
                    + $"{first.AppliedRevision}. Playing it on the wrong mod list is what damages a save.",

            _ => $"{save} no longer agrees with what the repo holds."
        };

        var more = drift.Count > 1
            ? $" {drift.Count - 1} more savegame problems here as well."
            : "";

        return $"{what}{more}";
    }

    private Repo? FindRepo(Guid repoId) => _repoRepository.Repos.FirstOrDefault(x => x.Id == repoId);
}
