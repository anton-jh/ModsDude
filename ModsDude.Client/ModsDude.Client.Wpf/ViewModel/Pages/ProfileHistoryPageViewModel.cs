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
    }


    public string ProfileName => _profile.Name;

    /// <summary>Whether restoring and branching are on offer at all. Reading a history is not.</summary>
    public bool CanEdit { get; }

    public ObservableCollection<ProfileRevisionViewModel> Revisions { get; }

    /// <summary>What the selected revision pinned.</summary>
    public ObservableCollection<PinnedModViewModel> Mods { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectedTitle))]
    [NotifyPropertyChangedFor(nameof(CanRestoreSelected))]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveAsCommand))]
    private ProfileRevisionViewModel? _selected;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _isLoadingMods;

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


    partial void OnSelectedChanged(ProfileRevisionViewModel? value)
    {
        if (value is not null)
        {
            _ = LoadModsAsync(value.Number);
        }
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


    public class Factory(IServiceProvider serviceProvider)
    {
        public ProfileHistoryPageViewModel Create(Repo repo, ProfileDto profile)
            => ActivatorUtilities.CreateInstance<ProfileHistoryPageViewModel>(serviceProvider, repo, profile);
    }
}
