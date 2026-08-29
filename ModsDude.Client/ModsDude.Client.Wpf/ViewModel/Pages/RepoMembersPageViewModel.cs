using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

public partial class RepoMembersPageViewModel : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly MembershipService _membershipService;
    private readonly IModalService _modalService;

    private IReadOnlyList<RepoMemberDto> _fetched = [];


    public RepoMembersPageViewModel(
        Repo repo,
        MembershipService membershipService,
        IModalService modalService)
    {
        _repo = repo;
        _membershipService = membershipService;
        _modalService = modalService;

        Members = [];

        // No level above the viewer's own can be handed out, and a guest cannot add anybody at all.
        GrantableLevels = [.. Enum.GetValues<RepoMembershipLevel>().Where(x => x <= repo.MembershipLevel)];
        _newMemberLevel = GrantableLevels.Contains(RepoMembershipLevel.Member)
            ? RepoMembershipLevel.Member
            : RepoMembershipLevel.Guest;
    }


    public string RepoName => _repo.Name;
    public ObservableCollection<RepoMemberViewModel> Members { get; }
    public IReadOnlyList<RepoMembershipLevel> GrantableLevels { get; }

    /// <summary>The member list itself is only readable from Member upwards, so a guest is told rather than shown an empty list.</summary>
    public bool CanSeeMembers => _repo.MembershipLevel >= RepoMembershipLevel.Member;

    public bool CanInvite => CanSeeMembers;

    public bool IsHiddenFromMe => !CanSeeMembers;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _searchTerm = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchMessage))]
    private string? _searchMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddMemberCommand))]
    [NotifyPropertyChangedFor(nameof(CanAddMember))]
    private UserDto? _addableUser;

    [ObservableProperty]
    private RepoMembershipLevel _newMemberLevel;

    public bool HasSearchMessage => !string.IsNullOrEmpty(SearchMessage);

    public bool CanSearch => !string.IsNullOrWhiteSpace(SearchTerm);

    public bool CanAddMember => AddableUser is not null;


    [RelayCommand(CanExecute = nameof(CanSearch))]
    public async Task Search(CancellationToken cancellationToken)
    {
        var username = SearchTerm.Trim();

        AddableUser = null;

        var user = await _membershipService.FindUser(username, cancellationToken);

        if (user is null)
        {
            SearchMessage = $"No user called '{username}'.";
            return;
        }

        if (Members.Any(x => x.UserId == user.Id))
        {
            SearchMessage = $"'{user.Username}' is already a member.";
            return;
        }

        SearchMessage = null;
        AddableUser = user;
    }

    [RelayCommand(CanExecute = nameof(CanAddMember))]
    public async Task AddMember(CancellationToken cancellationToken)
    {
        if (AddableUser is not UserDto user)
        {
            return;
        }

        await _membershipService.AddMember(_repo.Id, user.Id, NewMemberLevel, cancellationToken);

        AddableUser = null;
        SearchTerm = "";
        SearchMessage = null;

        await Reload(cancellationToken);
    }

    [RelayCommand]
    public Task Refresh(CancellationToken cancellationToken)
    {
        return Reload(cancellationToken);
    }

    public void Dispose()
    {
        ClearMembers();
    }


    protected override async Task InitAsync()
    {
        if (!CanSeeMembers)
        {
            return;
        }

        _fetched = await _membershipService.GetMembers(_repo.Id, CancellationToken.None);
    }

    protected override void OnInitCompleted()
    {
        Publish(_fetched);
    }


    partial void OnSearchTermChanged(string value)
    {
        AddableUser = null;
        SearchMessage = null;
    }

    private async Task Reload(CancellationToken cancellationToken)
    {
        if (!CanSeeMembers)
        {
            return;
        }

        Publish(await _membershipService.GetMembers(_repo.Id, cancellationToken));
    }

    private void Publish(IReadOnlyList<RepoMemberDto> members)
    {
        ClearMembers();

        _fetched = members;

        // Whether somebody is the last admin depends on the whole list, so the rows are rebuilt
        // together rather than patched. The list is short and nothing selects into it.
        var adminCount = members.Count(x => x.MembershipLevel is RepoMembershipLevel.Admin);

        foreach (var member in members.OrderBy(x => x.User.Username, StringComparer.CurrentCultureIgnoreCase))
        {
            var row = new RepoMemberViewModel(
                member,
                _repo.MembershipLevel,
                isOnlyAdmin: member.MembershipLevel is RepoMembershipLevel.Admin && adminCount == 1);

            row.LevelChangeRequested += OnLevelChangeRequested;
            row.KickRequested += OnKickRequested;

            Members.Add(row);
        }
    }

    private void ClearMembers()
    {
        foreach (var member in Members)
        {
            member.LevelChangeRequested -= OnLevelChangeRequested;
            member.KickRequested -= OnKickRequested;
        }

        Members.Clear();
    }

    private async void OnLevelChangeRequested(object? sender, RepoMembershipLevel level)
    {
        if (sender is not RepoMemberViewModel member)
        {
            return;
        }

        var previous = _fetched.FirstOrDefault(x => x.User.Id == member.UserId)?.MembershipLevel;

        try
        {
            await _membershipService.UpdateMembership(_repo.Id, member.UserId, level, CancellationToken.None);

            await Reload(CancellationToken.None);
        }
        catch (Exception ex)
        {
            if (previous is RepoMembershipLevel known)
            {
                member.RevertLevel(known);
            }

            await ShowError(ex);
        }
    }

    private async void OnKickRequested(object? sender, EventArgs e)
    {
        if (sender is not RepoMemberViewModel member)
        {
            return;
        }

        var modal = ConfirmationDialogViewModel.ConfirmDelete(member.Username);

        await _modalService.Show(modal);

        if (!modal.Result)
        {
            return;
        }

        try
        {
            await _membershipService.KickMember(_repo.Id, member.UserId, CancellationToken.None);

            await Reload(CancellationToken.None);
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


    public class Factory(IServiceProvider serviceProvider)
    {
        public RepoMembersPageViewModel Create(Repo repo)
            => ActivatorUtilities.CreateInstance<RepoMembersPageViewModel>(serviceProvider, repo);
    }
}
