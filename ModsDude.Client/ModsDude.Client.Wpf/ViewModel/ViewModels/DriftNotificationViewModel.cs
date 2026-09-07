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
/// </remarks>
public partial class DriftNotificationViewModel : ObservableObject, IDisposable
{
    private readonly InstanceDriftMonitor _monitor;
    private readonly RepoRepository _repoRepository;
    private readonly LocalInstanceRepository _instanceRepository;
    private readonly ProfileService _profileService;
    private readonly SavegameBindingStore _bindingStore;
    private readonly ProfileApplyService _applyService;
    private readonly ShellNavigationService _navigation;

    /// <summary>The one place the notice is suppressed: the drifted profile's own mod list editor.</summary>
    private readonly HashSet<ActiveProfile> _suppressed = [];

    private InstanceDrift? _subject;


    public DriftNotificationViewModel(
        InstanceDriftMonitor monitor,
        RepoRepository repoRepository,
        LocalInstanceRepository instanceRepository,
        ProfileService profileService,
        SavegameBindingStore bindingStore,
        ProfileApplyService applyService,
        ShellNavigationService navigation)
    {
        _monitor = monitor;
        _repoRepository = repoRepository;
        _instanceRepository = instanceRepository;
        _profileService = profileService;
        _bindingStore = bindingStore;
        _applyService = applyService;
        _navigation = navigation;

        _monitor.Changed += OnDriftChanged;
        _instanceRepository.Instances.CollectionChanged += OnInstancesChanged;

        // Drift is detected from the manifest and the folder, so this notice can be up before the
        // repo list has been fetched - it is raised from the window's constructor, and the repos are
        // loaded by a command on the shell underneath it. Everything the notice says about
        // membership is unknowable until they land, and nothing else would re-ask.
        _repoRepository.Repos.CollectionChanged += OnReposChanged;

        // Everything else that can change the answer, wired here rather than remembered at each call
        // site. A user who edits a profile, repoints an instance or checks a save out and then tabs
        // back to the game must not be the first to find out that they are out of sync - so the check
        // is driven by the facts changing, not by anybody remembering to ask.
        _instanceRepository.InstanceChanged += OnFactsChanged;
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
    /// Whether the mod half found anything to say. It can be empty - an instance reported only for
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
    /// moved makes an instance drifted on its own - see <c>InstanceDrift.IsDrifted</c> - so the notice
    /// could be raised entirely by it, and the detail line, which only ever described mod files and
    /// revisions, then had nothing to say. An empty mod folder on an empty profile is exactly that
    /// case, and it read as a warning with no reason in it.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSavegameWarning))]
    private string? _savegameWarning;

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
    /// Whether there is anything to re-apply. False for an instance reported only because it is
    /// holding a savegame and that has never been pointed at a profile - there is no mod list to put
    /// its folder back onto, and the buttons say so by not being there.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReapplyCommand))]
    [NotifyPropertyChangedFor(nameof(ReapplyIsPrimary))]
    [NotifyPropertyChangedFor(nameof(CanReapplyBesideReview))]
    private bool _canReapply = true;

    public bool ReapplyIsPrimary => CanReapply && CanReview is false;

    /// <summary>The plain Re-apply, which is only drawn where Review is leading beside it.</summary>
    public bool CanReapplyBesideReview => CanReapply && CanReview;

    public bool HasLockedWarning => LockedWarning is not null;
    public bool HasSavegameWarning => SavegameWarning is not null;
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
        _instanceRepository.Instances.CollectionChanged -= OnInstancesChanged;
        _repoRepository.Repos.CollectionChanged -= OnReposChanged;
        _instanceRepository.InstanceChanged -= OnFactsChanged;
        _profileService.ProfileUpdated -= OnProfileUpdated;
        _bindingStore.BindingsChanged -= OnFactsChanged;
    }


    [RelayCommand(CanExecute = nameof(CanReviewNow))]
    private async Task OpenModList()
    {
        if (_subject?.Instance.ActiveProfile is not ActiveProfile active)
        {
            return;
        }

        if (await _navigation.GoToProfileModsAsync(active.RepoId, active.ProfileId, _subject.Instance.InstanceId) is false)
        {
            Status = "That profile could not be opened from here - pick it in the sidebar.";
        }
    }

    /// <summary>
    /// One click, because most of the time there is nothing to change and the user just wants their
    /// locked versions back. Applies to every instance on the drifted profile, which is the derived
    /// target set and not a choice.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAct), IncludeCancelCommand = true)]
    private async Task Reapply(CancellationToken cancellationToken)
    {
        if (_subject?.Instance.ActiveProfile is not ActiveProfile active)
        {
            return;
        }

        if (FindRepo(active.RepoId) is not Repo repo)
        {
            // The same window the Review button is missing in: this notice can be up before the repo
            // list has arrived. Saying so beats a button that does nothing when pressed.
            Status = "The repo this instance follows has not loaded yet. Try again in a moment.";

            return;
        }

        IsBusy = true;
        Status = "Re-applying...";

        try
        {
            var messages = new List<string>();

            foreach (var instance in _instanceRepository.GetInstancesUsing(active))
            {
                var outcome = await _applyService.ApplyAsync(
                    repo,
                    instance,
                    active.ProfileId,
                    _subject.ProfileName,
                    confirmPlan: false,
                    progress: null,
                    cancellationToken);

                messages.Add(outcome.Message);
            }

            Status = messages.Count > 0 ? string.Join(" ", messages) : null;
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

    private void OnInstancesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // A folder that just arrived is not being watched yet, and one that left is being watched for
        // nothing.
        _monitor.Watch();

        _ = _monitor.CheckAsync();
    }

    /// <summary>
    /// Something the check reads has changed: an instance repointed, a profile that moved on, a
    /// savegame taken or handed back.
    /// </summary>
    /// <remarks>
    /// <see cref="DriftCheckReason.Explicit"/> by default, so the throttle never swallows one of
    /// these - they are consequences of something the user just did, and the whole complaint these
    /// answer is a notice that arrives one alt-tab too late.
    /// </remarks>
    private void OnFactsChanged(object? sender, EventArgs e)
    {
        // Re-watched as well as re-checked: an instance whose mod folder moved is being watched at
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
            .Where(x => x.Instance.ActiveProfile is not ActiveProfile active || _suppressed.Contains(active) is false)
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

        Headline = DescribeHeadline(drifted.Count, _subject.Instance.Name, profile, report);

        // Re-applying needs somewhere to apply *to*. An instance reported purely because it is holding
        // a savegame may have no active profile at all, and an accent button that returns the moment
        // it is pressed is worse than no button.
        CanReapply = _subject.Instance.ActiveProfile is not null;

        CanReview = _subject.Instance.ActiveProfile is ActiveProfile active
            && FindRepo(active.RepoId) is Repo repo
            && repo.MembershipLevel >= RepoMembershipLevel.Member;

        Detail = Describe(report, files);
        LockedWarning = DescribeLocked(report);
        SavegameWarning = DescribeSavegames(report);
        ImportPrompt = files > 0 && CanReview
            ? "The versions now on disk may not be in the repo. Opening the mod list is where they get imported."
            : null;

        IsVisible = true;

        ReapplyCommand.NotifyCanExecuteChanged();
        OpenModListCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// What the notice is about, in one line.
    /// </summary>
    /// <remarks>
    /// Three sentences rather than one, because an instance can be here for two unrelated reasons and
    /// the notice used to name only the first. A folder that matches its profile exactly and is
    /// holding somebody's evening in a slot was announced as "no longer matches the applied profile",
    /// followed by no detail at all - which is how a mod folder and a profile that are both empty
    /// produced a warning with nothing in it.
    /// </remarks>
    private static string DescribeHeadline(int count, string instanceName, string profile, InstanceDriftReport report)
    {
        if (count > 1)
        {
            return $"{count} game instances have drifted";
        }

        return (report.Status is InstanceDriftStatus.Drifted, report.HasSavegameDrift) switch
        {
            (true, true) => $"'{instanceName}' no longer matches {profile}, and its savegame has moved too",
            (false, true) => $"'{instanceName}' is holding a savegame that no longer agrees with the repo",
            _ => $"'{instanceName}' no longer matches {profile}"
        };
    }

    private static string Describe(InstanceDriftReport report, int files)
    {
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
    /// The half of drift no directory listing can find: the folder is exactly what was installed,
    /// and what was installed is no longer what the profile says. Two numbers, because that is all
    /// the cheap check has - and two numbers is enough to say something specific.
    /// </summary>
    private static string DescribeRevision(InstanceDriftReport report)
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
    private static string? DescribeLocked(InstanceDriftReport report)
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
    private static string? DescribeSavegames(InstanceDriftReport report)
    {
        if (report.SavegameDrift.Count == 0)
        {
            return null;
        }

        var first = report.SavegameDrift[0];
        var save = first.SlotDisplayName is { Length: > 0 } named ? $"'{named}'" : "A savegame checked out here";

        var what = first.Kind switch
        {
            SavegameDriftKind.UncheckedInPlay =>
                $"{save} has been played since it was checked out, and that play exists nowhere but this disk until it is checked in.",

            SavegameDriftKind.TakenOverAndCheckedIn =>
                $"{save} has been checked in by somebody else - they are on version {first.HeadVersion}, this machine is holding version "
                    + $"{first.HeldVersion}. Checking in from here forks it, and will be refused unless you force it.",

            SavegameDriftKind.PlayedOnAnotherModList =>
                $"{save} was checked out against revision {first.PlayedRevision} of its profile and this mod folder is on revision "
                    + $"{first.AppliedRevision}. Playing it on the wrong mod list is what damages a save.",

            _ => $"{save} no longer agrees with what the repo holds."
        };

        var more = report.SavegameDrift.Count > 1
            ? $" {report.SavegameDrift.Count - 1} more savegame problems here as well."
            : "";

        return $"{what}{more}";
    }

    private Repo? FindRepo(Guid repoId) => _repoRepository.Repos.FirstOrDefault(x => x.Id == repoId);
}
