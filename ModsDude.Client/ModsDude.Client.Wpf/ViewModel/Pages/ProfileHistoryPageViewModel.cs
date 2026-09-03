using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

/// <summary>
/// A profile's history: every revision on the left, what the selected one pinned on the right.
/// </summary>
/// <remarks>
/// <para>
/// Master and detail on one page rather than a list that navigates somewhere. Comparing "what did
/// it hold then" against "what does it hold now" is the whole reason to open a history, and a page
/// you have to leave to answer that makes it two acts of memory instead of one.
/// </para>
/// <para>
/// <b>Readable at Guest, actionable at Member.</b> Somebody who syncs this profile without curating
/// it is exactly the person who wants to know what changed under them - and, when a save breaks
/// their game, which revision to ask an editor to put back.
/// </para>
/// <para>
/// <b>Restoring copies forward.</b> It never deletes the revisions in between: what is on the
/// server stays on the server, which is what makes both this and an accidental overwrite
/// recoverable. See docs/02-domain-model.md#profile-revisions.
/// </para>
/// </remarks>
public partial class ProfileHistoryPageViewModel : PageViewModel
{
    private readonly Repo _repo;
    private readonly ProfileDto _profile;
    private readonly ProfileService _profileService;
    private readonly ModListItemViewModel.Factory _itemFactory;
    private readonly IModalService _modalService;

    private ProfileHistory? _fetched;


    public ProfileHistoryPageViewModel(
        Repo repo,
        ProfileDto profile,
        ProfileService profileService,
        ModListItemViewModel.Factory itemFactory,
        IModalService modalService)
    {
        _repo = repo;
        _profile = profile;
        _profileService = profileService;
        _itemFactory = itemFactory;
        _modalService = modalService;

        CanEdit = repo.MembershipLevel >= RepoMembershipLevel.Member;

        Revisions = [];
        Mods = [];
        Changes = [];
        ComparisonTargets = [];
    }


    public string ProfileName => _profile.Name;

    /// <summary>Whether restoring and branching are on offer at all. Reading a history is not.</summary>
    public bool CanEdit { get; }

    public ObservableCollection<ProfileRevisionViewModel> Revisions { get; }

    /// <summary>What the selected revision pinned.</summary>
    public ObservableCollection<PinnedModViewModel> Mods { get; }

    /// <summary>What changed between the selected revision and the one it is compared with.</summary>
    public ObservableCollection<ProfileModChangeViewModel> Changes { get; }

    /// <summary>
    /// The revisions the selected one can be compared with - every other one, newest first.
    /// </summary>
    public ObservableCollection<ProfileRevisionViewModel> ComparisonTargets { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectedTitle))]
    [NotifyPropertyChangedFor(nameof(CanRestoreSelected))]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveAsCommand))]
    private ProfileRevisionViewModel? _selected;

    /// <summary>
    /// Which question the right-hand pane is answering: what this revision held, or what it did.
    /// </summary>
    /// <remarks>
    /// Two views of one pane rather than two pages. "What did it hold" and "what changed" are asked
    /// about the same revision, seconds apart, and a comparison you have to navigate away to see
    /// makes it two acts of memory instead of one.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowContents))]
    private bool _showChanges;

    /// <summary>
    /// What the selected revision is compared with. Defaults to the revision before it, so the
    /// summary on its own row and this pane are describing the same thing.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ComparisonTitle))]
    private ProfileRevisionViewModel? _comparedWith;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _isLoadingMods;

    [ObservableProperty]
    private bool _isLoadingChanges;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoChanges))]
    private bool _comparisonIsEmpty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveAsCommand))]
    private bool _isWorking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _status;

    /// <summary>
    /// Set when the listing was windowed. Nothing pages further yet, and saying so beats a list that
    /// quietly stops - see docs/PLAN.md.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOlder))]
    private bool _hasMore;


    public bool HasSelection => Selected is not null;
    public bool HasStatus => Status is not null;
    public bool HasOlder => HasMore;

    public bool ShowContents => ShowChanges is false;

    public bool HasNoChanges => ComparisonIsEmpty && IsLoadingChanges is false;

    public string ComparisonTitle => ComparedWith is null
        ? "Nothing to compare with"
        : $"Compared with revision {ComparedWith.Number}";

    /// <summary>
    /// Two revisions can genuinely hold the same list - comparing a restore with what it restored is
    /// the ordinary way that happens - so this says which, rather than reading as a failure.
    /// </summary>
    public string NoChangesText => (Selected, ComparedWith) switch
    {
        (not null, null) => $"Revision {Selected.Number} is the first one; there is nothing before it to compare with.",
        (not null, not null) => $"Revisions {ComparedWith.Number} and {Selected.Number} pin exactly the same mods.",
        _ => ""
    };

    public string SelectedTitle => Selected is null
        ? ""
        : Selected.IsHead
            ? $"Revision {Selected.Number} · the current list"
            : $"Revision {Selected.Number}";

    /// <summary>
    /// Restoring the revision that is already current would record a restore that changes nothing.
    /// The server would accept it; offering it is what would be silly.
    /// </summary>
    public bool CanRestoreSelected => Selected is not null && Selected.IsHead is false;


    protected override async Task InitAsync()
    {
        _fetched = await _profileService.GetHistory(_repo.Id, _profile.Id, CancellationToken.None);
    }

    protected override void OnInitCompleted()
    {
        if (_fetched is not null)
        {
            Publish(_fetched);
        }

        IsLoading = false;
    }


    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task Restore(CancellationToken cancellationToken)
    {
        if (Selected is not ProfileRevisionViewModel revision)
        {
            return;
        }

        var confirmation = new ConfirmationDialogViewModel(
            $"Restore revision {revision.Number}?",
            "The profile goes back to what it pinned then, recorded as a new revision. Nothing is deleted - what it pins now stays in the history, so this can be undone the same way.",
            IconKind.Question,
            "Restore it",
            "Leave it");

        await _modalService.Show(confirmation);

        if (confirmation.Result is false)
        {
            return;
        }

        IsWorking = true;

        try
        {
            var restored = await _profileService.RestoreRevision(_repo.Id, _profile.Id, revision.Number, cancellationToken);

            Status = $"Restored revision {revision.Number} as revision {restored.Number}. Apply the profile to put it in your mod folder.";

            await ReloadAsync(select: restored.Number, cancellationToken);
        }
        finally
        {
            IsWorking = false;
        }
    }

    private bool CanRestore() => CanEdit && IsWorking is false && CanRestoreSelected;

    /// <summary>
    /// Branches the selected revision off into a profile of its own. The same primitive as a
    /// restore, pointed somewhere else - which is why an old revision being read-only costs nobody
    /// anything: taking it somewhere it can be edited is one dialog away.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSaveAs))]
    private async Task SaveAs(CancellationToken cancellationToken)
    {
        if (Selected is not ProfileRevisionViewModel revision)
        {
            return;
        }

        var modal = new ProfileSaveAsModalViewModel(revision.Number, $"{_profile.Name} (revision {revision.Number})");

        await _modalService.Show(modal);

        if (modal.Result is not string name)
        {
            return;
        }

        IsWorking = true;

        try
        {
            await _profileService.CreateProfile(
                _repo.Id,
                name,
                new CopyProfileRevisionRequest { ProfileId = _profile.Id, Revision = revision.Number },
                cancellationToken);

            Status = $"Created '{name}' from revision {revision.Number}. It is in the sidebar.";
        }
        finally
        {
            IsWorking = false;
        }
    }

    private bool CanSaveAs() => CanEdit && IsWorking is false && HasSelection;

    [RelayCommand]
    private async Task Refresh(CancellationToken cancellationToken)
        => await ReloadAsync(Selected?.Number, cancellationToken);

    /// <summary>
    /// The pane switch, as commands rather than a two-way bound flag: a radio button binding
    /// <c>IsChecked</c> two-way fires on the way out as well as the way in, so both halves of the
    /// pair would set the property and the second one would undo the first.
    /// </summary>
    [RelayCommand]
    private void ShowContentsView() => ShowChanges = false;

    [RelayCommand]
    private void ShowChangesView() => ShowChanges = true;


    partial void OnSelectedChanged(ProfileRevisionViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        RefreshComparisonTargets(value);

        _ = LoadModsAsync(value.Number);
    }

    partial void OnComparedWithChanged(ProfileRevisionViewModel? value)
    {
        _ = LoadChangesAsync();
    }

    partial void OnShowChangesChanged(bool value)
    {
        // Not loaded until it is looked at: a comparison is two more reads, and most visits to a
        // history only ever ask what a revision held.
        if (value)
        {
            _ = LoadChangesAsync();
        }
    }

    /// <summary>
    /// Every other revision, and by default the one immediately before the selected one - which is
    /// what the selected row's own summary counts describe, so the two agree until somebody asks a
    /// different question.
    /// </summary>
    private void RefreshComparisonTargets(ProfileRevisionViewModel selected)
    {
        ComparisonTargets.Clear();

        foreach (var revision in Revisions.Where(x => x.Number != selected.Number))
        {
            ComparisonTargets.Add(revision);
        }

        // Assigning this is what loads the comparison, so it is the last thing that happens here.
        ComparedWith = ComparisonTargets.FirstOrDefault(x => x.Number == selected.Number - 1)
            ?? ComparisonTargets.FirstOrDefault();
    }


    private async Task ReloadAsync(int? select, CancellationToken cancellationToken)
    {
        IsLoading = true;

        try
        {
            Publish(await _profileService.GetHistory(_repo.Id, _profile.Id, cancellationToken), select);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Publish(ProfileHistory history, int? select = null)
    {
        var wanted = select ?? Selected?.Number ?? history.HeadRevision;

        Revisions.Clear();

        foreach (var revision in history.Revisions)
        {
            Revisions.Add(new ProfileRevisionViewModel(revision, revision.Number == history.HeadRevision));
        }

        HasMore = history.HasMore;

        // Assigning this is what loads the mod list, so it happens after the rows exist rather than
        // as part of building them.
        Selected = Revisions.FirstOrDefault(x => x.Number == wanted) ?? Revisions.FirstOrDefault();
    }

    /// <summary>
    /// Reads one revision's mod list. Deliberately not cached: a history is walked a few rows at a
    /// time, and holding every revision's two thousand mods to save a request nobody made twice is
    /// the wrong trade.
    /// </summary>
    private async Task LoadModsAsync(int revision)
    {
        IsLoadingMods = true;

        try
        {
            var pinned = await _profileService.GetPinnedMods(_repo.Id, _profile.Id, revision, CancellationToken.None);

            // The selection can have moved on while this was in flight, in which case this answer is
            // about a revision nobody is looking at any more.
            if (Selected?.Number != revision)
            {
                return;
            }

            Mods.Clear();

            foreach (var mod in pinned)
            {
                Mods.Add(new PinnedModViewModel(mod, _repo.Id, _itemFactory));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Nothing awaits this - selecting a row starts it - so an exception that escaped would
            // go unobserved rather than reaching the shell's handler. Said on the page instead.
            Mods.Clear();
            Status = $"Could not read revision {revision}: {exception.Message}";
        }
        finally
        {
            IsLoadingMods = false;
        }
    }

    /// <summary>
    /// Compares the selected revision with the chosen one. Skipped entirely while the Contents view
    /// is showing, so picking through a history costs one read a row rather than three.
    /// </summary>
    private async Task LoadChangesAsync()
    {
        if (ShowChanges is false)
        {
            return;
        }

        if (Selected is not ProfileRevisionViewModel selected || ComparedWith is not ProfileRevisionViewModel against)
        {
            Changes.Clear();
            ComparisonIsEmpty = true;
            OnPropertyChanged(nameof(HasNoChanges));

            return;
        }

        IsLoadingChanges = true;

        try
        {
            var comparison = await _profileService.CompareRevisions(
                _repo.Id, _profile.Id, against.Number, selected.Number, CancellationToken.None);

            // The selection can have moved on while this was in flight, in which case this answer is
            // about a pair nobody is looking at any more.
            if (Selected?.Number != selected.Number || ComparedWith?.Number != against.Number)
            {
                return;
            }

            Changes.Clear();

            foreach (var change in comparison.Changes)
            {
                Changes.Add(new ProfileModChangeViewModel(change, _repo.Id, _itemFactory));
            }

            ComparisonIsEmpty = comparison.IsEmpty;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Changes.Clear();
            ComparisonIsEmpty = true;
            Status = $"Could not compare revisions {against.Number} and {selected.Number}: {exception.Message}";
        }
        finally
        {
            IsLoadingChanges = false;
            OnPropertyChanged(nameof(HasNoChanges));
        }
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public ProfileHistoryPageViewModel Create(Repo repo, ProfileDto profile)
            => ActivatorUtilities.CreateInstance<ProfileHistoryPageViewModel>(serviceProvider, repo, profile);
    }
}
