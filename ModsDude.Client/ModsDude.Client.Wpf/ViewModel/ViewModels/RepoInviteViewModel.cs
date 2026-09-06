using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using System.Globalization;
using System.Windows;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One invite on the admin page: the code to hand out, what it grants, what is left of it, and the
/// two things that can be done to it.
/// </summary>
/// <remarks>
/// <b>Revoked invites are gone; spent ones are not.</b> Revoking is one gesture - stop this code
/// working, take it off my list - so a retired code disappears the moment it is switched off. An
/// expired or exhausted one stays, because it is the only evidence the invite was made at all and
/// its absence would read as "I forgot to create one" rather than "it ran out". Removing one of
/// those is a separate, deliberate act, and it is the same button in a different tense.
/// </remarks>
public partial class RepoInviteViewModel : ObservableObject
{
    public RepoInviteViewModel(RepoInviteDto invite, bool canRemove)
    {
        Id = invite.Id;
        Code = invite.Code;
        Level = invite.MembershipLevel;
        Status = invite.Status;
        IsActive = invite.Status is InviteStatus.Active;

        // Offered on a spent invite too, where it means "take this off the list" rather than "stop
        // this working". One button, because it is one wish either way.
        CanRemove = canRemove;

        ActionText = IsActive ? "Revoke" : "Remove";
        ActionToolTip = IsActive
            ? "Stops the code working for good, and takes it off this list."
            : "Takes it off this list. It already stopped working.";

        Uses = invite.MaximumUses is int maximum
            ? $"{invite.Uses} of {maximum} joins"
            : Describe(invite.Uses);

        Limit = DescribeLimit(invite);
    }


    /// <summary>
    /// Raised when the user asks for this invite to go away. The page confirms and does it, and what
    /// "away" means is decided by the server from the invite's own state rather than here.
    /// </summary>
    public event EventHandler? RemovalRequested;


    public Guid Id { get; }

    /// <summary>Dashed into threes of four, exactly as it is copied and read out.</summary>
    public string Code { get; }

    public RepoMembershipLevel Level { get; }
    public InviteStatus Status { get; }
    public bool IsActive { get; }
    public bool CanRemove { get; }

    /// <summary>What the row's one button says, which is the only thing that differs between the two.</summary>
    public string ActionText { get; }

    public string ActionToolTip { get; }

    /// <summary>Successful joins, against the cap if there is one.</summary>
    public string Uses { get; }

    /// <summary>What is left of the invite in words - its expiry, or why it no longer works.</summary>
    public string Limit { get; }

    [ObservableProperty]
    private bool _isCopied;


    [RelayCommand]
    public void Copy()
    {
        // The clipboard belongs to whatever else on the machine is holding it open, and losing that
        // race is not worth an error modal - the code is on screen to be read either way.
        try
        {
            Clipboard.SetDataObject(Code, copy: true);
            IsCopied = true;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            IsCopied = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    public void RequestRemoval()
    {
        RemovalRequested?.Invoke(this, EventArgs.Empty);
    }


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
