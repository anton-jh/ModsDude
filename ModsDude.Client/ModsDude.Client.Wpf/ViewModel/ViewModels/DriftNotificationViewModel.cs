using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
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
    private readonly ProfileApplyService _applyService;
    private readonly ShellNavigationService _navigation;

    /// <summary>The one place the notice is suppressed: the drifted profile's own mod list editor.</summary>
    private readonly HashSet<ActiveProfile> _suppressed = [];

    private InstanceDrift? _subject;


    public DriftNotificationViewModel(
        InstanceDriftMonitor monitor,
        RepoRepository repoRepository,
        LocalInstanceRepository instanceRepository,
        ProfileApplyService applyService,
        ShellNavigationService navigation)
    {
        _monitor = monitor;
        _repoRepository = repoRepository;
        _instanceRepository = instanceRepository;
        _applyService = applyService;
        _navigation = navigation;

        _monitor.Changed += OnDriftChanged;
        _instanceRepository.Instances.CollectionChanged += OnInstancesChanged;

        // Drift is detected from the manifest and the folder, so this notice can be up before the
        // repo list has been fetched - it is raised from the window's constructor, and the repos are
        // loaded by a command on the shell underneath it. Everything the notice says about
        // membership is unknowable until they land, and nothing else would re-ask.
        _repoRepository.Repos.CollectionChanged += OnReposChanged;
    }


    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private string _headline = "";

    [ObservableProperty]
    private string _detail = "";

    /// <summary>
    /// Named separately from the count on purpose. An unlocked mod at the wrong version is untidy; a
    /// locked map at the wrong version is a damaged savegame waiting to happen.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLockedWarning))]
    private string? _lockedWarning;

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
    private bool _canReview;

    /// <summary>
    /// Whether re-applying is the notice's leading action. It is not, wherever Review is offered -
    /// looking first is the better move, and re-applying is what discards what the game downloaded.
    /// For a guest there is nothing else to offer, so it leads by being the only one.
    /// </summary>
    public bool ReapplyIsPrimary => CanReview is false;

    public bool HasLockedWarning => LockedWarning is not null;
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

    public void Release(ActiveProfile profile)
    {
        _suppressed.Remove(profile);

        Refresh();
    }

    public void Dispose()
    {
        _monitor.Changed -= OnDriftChanged;
        _instanceRepository.Instances.CollectionChanged -= OnInstancesChanged;
        _repoRepository.Repos.CollectionChanged -= OnReposChanged;
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

    private bool CanAct() => IsBusy is false;

    private bool CanReviewNow() => CanAct() && CanReview;

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

        Headline = drifted.Count == 1
            ? $"'{_subject.Instance.Name}' no longer matches {profile}"
            : $"{drifted.Count} game instances no longer match their profiles";

        CanReview = _subject.Instance.ActiveProfile is ActiveProfile active
            && FindRepo(active.RepoId) is Repo repo
            && repo.MembershipLevel >= RepoMembershipLevel.Member;

        Detail = Describe(report, files);
        LockedWarning = DescribeLocked(report);
        ImportPrompt = files > 0 && CanReview
            ? "The versions now on disk may not be in the repo. Opening the mod list is where they get imported."
            : null;

        IsVisible = true;

        ReapplyCommand.NotifyCanExecuteChanged();
        OpenModListCommand.NotifyCanExecuteChanged();
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

        return string.Join(' ', new[] { folder, pins }.Where(x => x.Length > 0));
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

    private Repo? FindRepo(Guid repoId) => _repoRepository.Repos.FirstOrDefault(x => x.Id == repoId);
}
