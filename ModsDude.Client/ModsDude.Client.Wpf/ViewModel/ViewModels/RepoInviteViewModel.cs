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
/// Spent invites stay in the list rather than disappearing. The join count is the only record of who
/// came in through which code, and it would be thrown away by hiding a code the moment it stopped
/// working.
/// </remarks>
public partial class RepoInviteViewModel : ObservableObject
{
    public RepoInviteViewModel(RepoInviteDto invite, bool canRevoke)
    {
        Id = invite.Id;
        Code = invite.Code;
        Level = invite.MembershipLevel;
        Status = invite.Status;
        IsActive = invite.Status is InviteStatus.Active;
        CanRevoke = canRevoke && IsActive;

        Uses = invite.MaximumUses is int maximum
            ? $"{invite.Uses} of {maximum} joins"
            : Describe(invite.Uses);

        Limit = DescribeLimit(invite);
    }


    /// <summary>Raised when the user asks for this invite to be revoked. The page confirms and does it.</summary>
    public event EventHandler? RevokeRequested;


    public Guid Id { get; }

    /// <summary>Dashed into threes of four, exactly as it is copied and read out.</summary>
    public string Code { get; }

    public RepoMembershipLevel Level { get; }
    public InviteStatus Status { get; }
    public bool IsActive { get; }
    public bool CanRevoke { get; }

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

    [RelayCommand(CanExecute = nameof(CanRevoke))]
    public void RequestRevoke()
    {
        RevokeRequested?.Invoke(this, EventArgs.Empty);
    }


    private static string Describe(int uses)
    {
        return uses == 1 ? "1 join" : $"{uses} joins";
    }

    private static string DescribeLimit(RepoInviteDto invite)
    {
        return invite.Status switch
        {
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
