using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Users;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One row of a repo's member list, and what the signed-in user is allowed to do to it. The server
/// enforces every rule here; the point of repeating them is to not offer an action that can only
/// fail.
/// </summary>
/// <remarks>
/// The level picker holds its change rather than sending it. A dropdown that saves on selection
/// fires on the way past every level between the old one and the intended one when opened with a
/// keyboard, and offers no way to change your mind; the card's Save button is what commits.
/// </remarks>
public partial class RepoMemberViewModel : ObservableObject
{
    private readonly RepoMembershipLevel _originalLevel;


    /// <param name="isAmbiguous">
    /// Whether somebody else in this same list is called the same thing. It is the list that decides
    /// it, not the member, which is why it arrives from outside rather than being worked out here.
    /// </param>
    /// <param name="isSelf">Whether this row is the signed-in user, which changes what removing means.</param>
    public RepoMemberViewModel(
        RepoMemberDto member,
        RepoMembershipLevel viewerLevel,
        bool isOnlyAdmin,
        bool isAmbiguous,
        bool isSelf)
    {
        UserId = member.User.Id;
        DisplayName = member.User.DisplayName;
        Tag = member.User.Tag;
        ShowTag = isAmbiguous;
        AvatarColor = UserDisplay.ColorFor(member.User.Tag);
        Initial = UserDisplay.InitialFor(member.User.DisplayName);
        IsOnlyAdmin = isOnlyAdmin;
        IsSelf = isSelf;

        _originalLevel = member.MembershipLevel;
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

        // Leaving is the same call as being removed, and it is refused for the same reason, so the
        // button is one button - it just has to say which of the two it is about to do.
        KickLabel = isSelf ? "Leave" : "Remove";

        KickRestriction = isOnlyAdmin
            ? isSelf
                ? "You are the only admin. Promote somebody else before you leave."
                : "The only admin cannot be removed. Promote somebody else first."
            : mayChange
                ? null
                : "You do not have the level to change this membership.";

        LevelRestriction = isOnlyAdmin
            ? "The only admin cannot be demoted. Promote somebody else first."
            : mayChange
                ? null
                : "You do not have the level to change this membership.";
    }


    /// <summary>Raised when the user asks for this member to be removed. The page confirms and does it.</summary>
    public event EventHandler? KickRequested;


    public string UserId { get; }

    /// <summary>Exactly what this person is called. Never edited to make room for anybody else.</summary>
    public string DisplayName { get; }

    /// <summary>Four digits, theirs everywhere. Only worth drawing when <see cref="ShowTag"/>.</summary>
    public string Tag { get; }
    public bool ShowTag { get; }

    /// <summary>Hex, bound straight onto a brush. Always drawn - it is an avatar, not a warning.</summary>
    public string AvatarColor { get; }
    public string Initial { get; }

    public bool IsOnlyAdmin { get; }
    public bool IsSelf { get; }
    public IReadOnlyList<RepoMembershipLevel> AvailableLevels { get; }
    public bool CanChangeLevel { get; }
    public bool CanKick { get; }

    /// <summary>"Leave" on your own row, "Remove" on anybody else's.</summary>
    public string KickLabel { get; }

    /// <summary>Why that button is off, shown on the button itself. Null while it works.</summary>
    public string? KickRestriction { get; }

    /// <summary>Why the level picker is off. Null while it works.</summary>
    public string? LevelRestriction { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingLevelChange))]
    private RepoMembershipLevel _level;

    /// <summary>Whether the picker is showing something the server has not been told about yet.</summary>
    public bool HasPendingLevelChange => Level != _originalLevel;

    /// <summary>The level as the server has it, which is what a save has to send and a reset restores.</summary>
    public RepoMembershipLevel OriginalLevel => _originalLevel;


    [RelayCommand(CanExecute = nameof(CanKick))]
    public void RequestKick()
    {
        KickRequested?.Invoke(this, EventArgs.Empty);
    }


    private static RepoMembershipLevel RequiredToChange(RepoMembershipLevel subjectLevel)
    {
        return subjectLevel is RepoMembershipLevel.Guest
            ? RepoMembershipLevel.Member
            : RepoMembershipLevel.Admin;
    }
}
