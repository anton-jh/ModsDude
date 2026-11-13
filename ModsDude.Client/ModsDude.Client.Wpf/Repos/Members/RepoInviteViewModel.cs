using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using System.Globalization;

namespace ModsDude.Client.Wpf.Repos.Members;

/// <summary>
/// One invite on the members page: who made it, what it grants, what is left of it, and what can be
/// done to it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The code is its maker's alone.</b> The list carries none; copying asks the server for it, which
/// answers only the person who made the invite.
/// </para>
/// <para>
/// <b>Revoked invites are gone; spent ones are not.</b> Revoking is one gesture - stop this code
/// working, take it off my list - so a retired code disappears the moment it is switched off. An
/// expired or exhausted one stays, because it is the only evidence the invite was made at all and
/// its absence would read as "I forgot to create one" rather than "it ran out". Removing one of
/// those is a separate, deliberate act, and it is the same button in a different tense.
/// </para>
/// </remarks>
public partial class RepoInviteViewModel : ObservableObject
{
    public RepoInviteViewModel(
        RepoInviteDto invite,
        bool isMine,
        bool canRemove,
        Func<RepoInviteViewModel, Task> copy,
        Func<RepoInviteViewModel, Task> remove)
    {
        _copy = copy;
        _remove = remove;
        Id = invite.Id;
        Level = invite.MembershipLevel;
        Status = invite.Status;
        IsActive = invite.Status is InviteStatus.Active;
        IsMine = isMine;
        CreatedBy = invite.CreatedBy?.DisplayName ?? "Operator";

        // Offered on a spent invite too, where it means "take this off the list" rather than "stop
        // this working". One button, because it is one wish either way. One stopped by its inviter's
        // access is revoked, because that access can come back.
        CanRemove = canRemove;
        WillRevoke = invite.Status is InviteStatus.Active or InviteStatus.InviterLostAccess;

        ActionText = WillRevoke ? "Revoke" : "Remove";
        ActionToolTip = WillRevoke
            ? "Stops the code working for good, and takes it off this list."
            : "Takes it off this list. It already stopped working.";

        Uses = invite.MaximumUses is int maximum
            ? $"{invite.Uses} of {maximum} joins"
            : Describe(invite.Uses);

        Limit = DescribeLimit(invite);
    }


    private readonly Func<RepoInviteViewModel, Task> _copy;

    /// <summary>
    /// What the remove button runs. The page confirms and does it, and what "away" means is decided by
    /// the server from the invite's own state rather than here.
    /// </summary>
    private readonly Func<RepoInviteViewModel, Task> _remove;


    public Guid Id { get; }
    public RepoMembershipLevel Level { get; }
    public InviteStatus Status { get; }
    public bool IsActive { get; }

    /// <summary>Whether the caller made it, which is what lets them copy its code.</summary>
    public bool IsMine { get; }

    public string CreatedBy { get; }
    public bool CanRemove { get; }

    /// <summary>Whether removing it revokes it, rather than only taking a spent one off the list.</summary>
    public bool WillRevoke { get; }

    /// <summary>What the row's one button says, which is the only thing that differs between the two.</summary>
    public string ActionText { get; }

    public string ActionToolTip { get; }

    /// <summary>Successful joins, against the cap if there is one.</summary>
    public string Uses { get; }

    /// <summary>What is left of the invite in words - its expiry, or why it no longer works.</summary>
    public string Limit { get; }

    [ObservableProperty]
    private bool _isCopied;


    [RelayCommand(CanExecute = nameof(IsMine))]
    private Task Copy() => _copy(this);

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private Task RequestRemoval() => _remove(this);


    private static string Describe(int uses)
    {
        return uses == 1 ? "1 join" : $"{uses} joins";
    }

    private static string DescribeLimit(RepoInviteDto invite)
    {
        return invite.Status switch
        {
            // Filtered out by the server, so this is defensive rather than reachable - revoking
            // takes an invite off the list in the same act.
            InviteStatus.Revoked => "Revoked",
            InviteStatus.Expired => $"Expired {Format(invite.ExpiresAt)}",
            InviteStatus.Exhausted => "All joins used",
            InviteStatus.InviterLostAccess => "Inviter lost access",
            _ when invite.ExpiresAt is DateTime expiry => $"Expires {Format(expiry)}",
            _ => "No expiry"
        };
    }

    private static string Format(DateTime? moment)
    {
        return moment is DateTime value
            ? value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
            : "";
    }
}
