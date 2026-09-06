using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Users;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

/// <summary>
/// Who is in the repo, and the codes that let anybody else in. One page, because they are one
/// question: an invite is how the list below it grows, and the list is how you check that it did.
/// </summary>
public partial class RepoMembersPageViewModel : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly MembershipService _membershipService;
    private readonly InviteService _inviteService;
    private readonly RepoRepository _repoRepository;
    private readonly CurrentUserService _currentUserService;
    private readonly NavigationLockService _navigationLockService;
    private readonly IModalService _modalService;

    private IReadOnlyList<RepoMemberDto> _fetchedMembers = [];
    private IReadOnlyList<RepoInviteDto> _fetchedInvites = [];
    private string? _currentUserId;


    public RepoMembersPageViewModel(
        Repo repo,
        MembershipService membershipService,
        InviteService inviteService,
        RepoRepository repoRepository,
        CurrentUserService currentUserService,
        NavigationLockService navigationLockService,
        IModalService modalService)
    {
        _repo = repo;
        _membershipService = membershipService;
        _inviteService = inviteService;
        _repoRepository = repoRepository;
        _currentUserService = currentUserService;
        _navigationLockService = navigationLockService;
        _modalService = modalService;

        Members = [];
        Invites = [];

        // No level above the viewer's own can be handed out, and an invite can never carry Admin
        // however senior its author - see RepoInvite. A guest cannot invite at all.
        GrantableLevels =
        [
            .. Enum.GetValues<RepoMembershipLevel>()
                .Where(x => x <= repo.MembershipLevel && x < RepoMembershipLevel.Admin)
        ];
        _newInviteLevel = GrantableLevels.Contains(RepoMembershipLevel.Member)
            ? RepoMembershipLevel.Member
            : RepoMembershipLevel.Guest;

        ExpiryOptions =
        [
            new("Never", null),
            new("1 hour", TimeSpan.FromHours(1)),
            new("1 day", TimeSpan.FromDays(1)),
            new("7 days", TimeSpan.FromDays(7)),
            new("30 days", TimeSpan.FromDays(30))
        ];
        _newInviteExpiry = ExpiryOptions[0];
    }


    public string RepoName => _repo.Name;
    public ObservableCollection<RepoMemberViewModel> Members { get; }
    public ObservableCollection<RepoInviteViewModel> Invites { get; }

    /// <summary>The member list itself is only readable from Member upwards, so a guest is told rather than shown an empty list.</summary>
    public bool CanSeeMembers => _repo.MembershipLevel >= RepoMembershipLevel.Member;

    /// <summary>
    /// Inviting is a Member's job as much as an Admin's, and it is the only way anybody joins - so a
    /// repo whose one admin is away is not a repo nobody can be let into.
    /// </summary>
    public bool CanManageInvites => _repo.MembershipLevel >= RepoMembershipLevel.Member;

    public bool IsHiddenFromMe => !CanSeeMembers;

    public IReadOnlyList<RepoMembershipLevel> GrantableLevels { get; }
    public IReadOnlyList<InviteExpiryOption> ExpiryOptions { get; }

    /// <summary>Whether any row's picker is showing a level the server has not been told about.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveLevelsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscardLevelsCommand))]
    private bool _hasPendingLevelChanges;

    [ObservableProperty]
    private RepoMembershipLevel _newInviteLevel;

    [ObservableProperty]
    private InviteExpiryOption _newInviteExpiry;

    /// <summary>Blank means no cap. Kept as text so that "no answer" and "zero" stay different things.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateInviteCommand))]
    private string _newInviteMaximumUses = "";

    public bool CanCreateInvite => CanManageInvites && TryReadMaximumUses(out _);


    /// <summary>
    /// Sends every changed level. One round trip each, because that is the endpoint - but one
    /// deliberate action by the user, which is what the picker on its own could not offer.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasPendingLevelChanges))]
    public async Task SaveLevels(CancellationToken cancellationToken)
    {
        var pending = Members.Where(x => x.HasPendingLevelChange).ToList();

        foreach (var member in pending)
        {
            try
            {
                await _membershipService.UpdateMembership(_repo.Id, member.UserId, member.Level, cancellationToken);
            }
            catch (Exception ex)
            {
                // Whatever went through stays through. Reloading is what makes the rows agree with
                // the server again rather than leaving half the edits looking unsaved.
                await ShowError(ex);
                break;
            }
        }

        await ReloadMembers(cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(HasPendingLevelChanges))]
    public void DiscardLevels()
    {
        foreach (var member in Members)
        {
            member.Level = member.OriginalLevel;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreateInvite))]
    public async Task CreateInvite(CancellationToken cancellationToken)
    {
        if (!TryReadMaximumUses(out var maximumUses))
        {
            return;
        }

        await _inviteService.CreateInvite(
            _repo.Id,
            NewInviteLevel,
            maximumUses,
            NewInviteExpiry.Duration is TimeSpan duration ? DateTime.UtcNow + duration : null,
            cancellationToken);

        NewInviteMaximumUses = "";

        await ReloadInvites(cancellationToken);
    }

    /// <summary>Both halves at once: an invite that was just redeemed is a member who was just added.</summary>
    [RelayCommand]
    public async Task Refresh(CancellationToken cancellationToken)
    {
        await ReloadMembers(cancellationToken);
        await ReloadInvites(cancellationToken);
    }

    public void Dispose()
    {
        _navigationLockService.ReleaseLock(this);

        ClearMembers();
        ClearInvites();
    }


    /// <summary>
    /// Unsaved level picks take the same global lock an editor's text box does, so navigating away
    /// asks before throwing them away. Released the moment nothing is pending - which a save, a
    /// discard and a reload all arrive at through <see cref="RecountPendingLevelChanges"/>.
    /// </summary>
    partial void OnHasPendingLevelChangesChanged(bool value)
    {
        if (value)
        {
            _navigationLockService.AcquireLock(this);
        }
        else
        {
            _navigationLockService.ReleaseLock(this);
        }
    }


    protected override async Task InitAsync()
    {
        if (!CanSeeMembers)
        {
            return;
        }

        // Which row is the caller's own decides whether its button says Remove or Leave, and the
        // member list does not say - it describes everybody the same way.
        _currentUserId = (await _currentUserService.Get(CancellationToken.None)).Id;

        _fetchedMembers = await _membershipService.GetMembers(_repo.Id, CancellationToken.None);
        _fetchedInvites = await _inviteService.GetInvites(_repo.Id, CancellationToken.None);
    }

    protected override void OnInitCompleted()
    {
        PublishMembers(_fetchedMembers);
        PublishInvites(_fetchedInvites);
    }


    /// <summary>Blank is a cap of none; anything else has to be a whole number of joins above zero.</summary>
    private bool TryReadMaximumUses(out int? maximumUses)
    {
        maximumUses = null;

        if (string.IsNullOrWhiteSpace(NewInviteMaximumUses))
        {
            return true;
        }

        if (!int.TryParse(NewInviteMaximumUses.Trim(), NumberStyles.None, CultureInfo.CurrentCulture, out var parsed)
            || parsed <= 0)
        {
            return false;
        }

        maximumUses = parsed;
        return true;
    }

    private async Task ReloadMembers(CancellationToken cancellationToken)
    {
        if (!CanSeeMembers)
        {
            return;
        }

        PublishMembers(await _membershipService.GetMembers(_repo.Id, cancellationToken));
    }

    private async Task ReloadInvites(CancellationToken cancellationToken)
    {
        if (!CanManageInvites)
        {
            return;
        }

        PublishInvites(await _inviteService.GetInvites(_repo.Id, cancellationToken));
    }

    private void PublishMembers(IReadOnlyList<RepoMemberDto> members)
    {
        ClearMembers();

        _fetchedMembers = members;

        // Whether somebody is the last admin depends on the whole list, so the rows are rebuilt
        // together rather than patched. The list is short and nothing selects into it.
        var adminCount = members.Count(x => x.MembershipLevel is RepoMembershipLevel.Admin);

        // As does whether their name needs a tag beside it. Two Antons in this repo both get one;
        // an Anton who is the only one here gets none, even if the server knows of others.
        var ambiguous = UserDisplay.FindAmbiguous(members.Select(x => x.User));

        foreach (var member in members.OrderBy(x => x.User.DisplayName, NaturalOrder.Comparer))
        {
            var row = new RepoMemberViewModel(
                member,
                _repo.MembershipLevel,
                isOnlyAdmin: member.MembershipLevel is RepoMembershipLevel.Admin && adminCount == 1,
                isAmbiguous: ambiguous.Contains(member.User.Id),
                isSelf: member.User.Id == _currentUserId);

            row.PropertyChanged += OnMemberChanged;
            row.KickRequested += OnKickRequested;

            Members.Add(row);
        }

        RecountPendingLevelChanges();
    }

    private void PublishInvites(IReadOnlyList<RepoInviteDto> invites)
    {
        ClearInvites();

        _fetchedInvites = invites;

        // Live ones first: a code somebody is about to read out matters more than the record of one
        // that has already done its work.
        foreach (var invite in invites
            .OrderByDescending(x => x.Status is InviteStatus.Active)
            .ThenByDescending(x => x.Created))
        {
            var row = new RepoInviteViewModel(invite, CanManageInvites);

            row.RevokeRequested += OnRevokeRequested;

            Invites.Add(row);
        }
    }

    private void ClearMembers()
    {
        foreach (var member in Members)
        {
            member.PropertyChanged -= OnMemberChanged;
            member.KickRequested -= OnKickRequested;
        }

        Members.Clear();
    }

    private void ClearInvites()
    {
        foreach (var invite in Invites)
        {
            invite.RevokeRequested -= OnRevokeRequested;
        }

        Invites.Clear();
    }

    private void OnMemberChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RepoMemberViewModel.HasPendingLevelChange))
        {
            RecountPendingLevelChanges();
        }
    }

    private void RecountPendingLevelChanges()
    {
        HasPendingLevelChanges = Members.Any(x => x.HasPendingLevelChange);
    }

    private async void OnKickRequested(object? sender, EventArgs e)
    {
        if (sender is not RepoMemberViewModel member)
        {
            return;
        }

        var modal = member.IsSelf
            ? new ConfirmationDialogViewModel(
                $"Leave {_repo.Name}?",
                "You lose access to its mods and profiles. Getting back in needs a new invite from somebody still in it.",
                IconKind.Warning,
                "Leave",
                "Stay")
            : ConfirmationDialogViewModel.ConfirmDelete(member.DisplayName);

        await _modalService.Show(modal);

        if (!modal.Result)
        {
            return;
        }

        try
        {
            await _membershipService.KickMember(_repo.Id, member.UserId, CancellationToken.None);

            if (member.IsSelf)
            {
                // This page and the repo it belongs to are about to stop existing for this user, so
                // there is nothing here to reload - refreshing the shell's list is what takes them
                // both away.
                await _repoRepository.RefreshRepos(CancellationToken.None);
                return;
            }

            await ReloadMembers(CancellationToken.None);
        }
        catch (Exception ex)
        {
            await ShowError(ex);
        }
    }

    private async void OnRevokeRequested(object? sender, EventArgs e)
    {
        if (sender is not RepoInviteViewModel invite)
        {
            return;
        }

        var modal = new ConfirmationDialogViewModel(
            "Revoke this invite?",
            $"{invite.Code} will stop working for good. Anybody who already joined with it stays a member.",
            IconKind.Warning,
            "Revoke",
            "Keep it");

        await _modalService.Show(modal);

        if (!modal.Result)
        {
            return;
        }

        try
        {
            await _inviteService.RevokeInvite(_repo.Id, invite.Id, CancellationToken.None);

            await ReloadInvites(CancellationToken.None);
        }
        catch (Exception ex)
        {
            await ShowError(ex);
        }
    }

    /// <summary>
    /// The rows raise plain events rather than running commands, so a failure here has no command to
    /// carry it to the global handler and has to reach the user itself.
    /// </summary>
    private Task ShowError(Exception ex)
    {
        return _modalService.Show(ConfirmationDialogViewModel.Error(
            ex as UserFriendlyException ?? UserFriendlyException.WrapUnknown(ex)));
    }


    /// <summary>How long a new invite should last, offered as durations rather than as a calendar.</summary>
    public record InviteExpiryOption(string Label, TimeSpan? Duration)
    {
        public override string ToString() => Label;
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public RepoMembersPageViewModel Create(Repo repo)
            => ActivatorUtilities.CreateInstance<RepoMembersPageViewModel>(serviceProvider, repo);
    }
}
