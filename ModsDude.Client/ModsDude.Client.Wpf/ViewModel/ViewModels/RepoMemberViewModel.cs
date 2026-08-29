using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One row of a repo's member list, and what the signed-in user is allowed to do to it. The server
/// enforces every rule here; the point of repeating them is to not offer an action that can only
/// fail.
/// </summary>
public partial class RepoMemberViewModel : ObservableObject
{
    private bool _applyingLevel;


    public RepoMemberViewModel(RepoMemberDto member, RepoMembershipLevel viewerLevel, bool isOnlyAdmin)
    {
        UserId = member.User.Id;
        Username = member.User.Username;
        IsOnlyAdmin = isOnlyAdmin;

        _level = member.MembershipLevel;

        // Changing somebody's membership needs Member for a guest and Admin for anybody else, and
        // no level above the viewer's own can be handed out.
        var mayChange = viewerLevel >= RequiredToChange(member.MembershipLevel);

        AvailableLevels =
        [
            .. Enum.GetValues<RepoMembershipLevel>()
                .Where(x => x <= viewerLevel || x == member.MembershipLevel)
        ];

        CanChangeLevel = mayChange && !isOnlyAdmin && AvailableLevels.Count > 1;
        CanKick = mayChange && !isOnlyAdmin;

        Restriction = isOnlyAdmin
            ? "The only admin - promote somebody else first"
            : mayChange
                ? null
                : "You do not have the level to change this membership";
    }


    /// <summary>Raised when the user picks a different level. The page owns the round trip.</summary>
    public event EventHandler<RepoMembershipLevel>? LevelChangeRequested;

    /// <summary>Raised when the user asks for this member to be removed. The page confirms and does it.</summary>
    public event EventHandler? KickRequested;


    public string UserId { get; }
    public string Username { get; }
    public bool IsOnlyAdmin { get; }
    public IReadOnlyList<RepoMembershipLevel> AvailableLevels { get; }
    public bool CanChangeLevel { get; }
    public bool CanKick { get; }
    public string? Restriction { get; }
    public bool HasRestriction => Restriction is not null;

    [ObservableProperty]
    private RepoMembershipLevel _level;


    [RelayCommand(CanExecute = nameof(CanKick))]
    public void RequestKick()
    {
        KickRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Puts the level back without asking the server again, after a change it refused.</summary>
    public void RevertLevel(RepoMembershipLevel level)
    {
        _applyingLevel = true;
        Level = level;
        _applyingLevel = false;
    }


    partial void OnLevelChanged(RepoMembershipLevel value)
    {
        if (_applyingLevel)
        {
            return;
        }

        LevelChangeRequested?.Invoke(this, value);
    }


    private static RepoMembershipLevel RequiredToChange(RepoMembershipLevel subjectLevel)
    {
        return subjectLevel is RepoMembershipLevel.Guest
            ? RepoMembershipLevel.Member
            : RepoMembershipLevel.Admin;
    }
}
