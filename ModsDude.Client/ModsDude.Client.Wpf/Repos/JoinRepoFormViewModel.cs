using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Wpf.Repos;

/// <summary>
/// The way into somebody else's repo: paste the code they sent you.
/// </summary>
/// <remarks>
/// There is nothing to search and nobody to be found. A user is reachable only through a code they
/// were handed, which is what makes it safe for two people to be called the same thing - and what
/// stops anybody being added to a repo they never asked to be in.
/// </remarks>
public partial class JoinRepoFormViewModel(
    IInviteService inviteService,
    ILogger<JoinRepoFormViewModel> logger)
    : ObservableObject
{
    /// <summary>
    /// Taken as typed. The server accepts any casing, any spacing and the letters people reach for
    /// in place of digits, so nothing is corrected on the way out of this box.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(JoinCommand))]
    private string _code = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasJoined))]
    private string? _joinedRepoName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool HasJoined => JoinedRepoName is not null;

    public bool HasError => Error is not null;

    public bool CanJoin => !string.IsNullOrWhiteSpace(Code);


    [RelayCommand(CanExecute = nameof(CanJoin))]
    private async Task Join(CancellationToken cancellationToken)
    {
        RepoMembershipDto membership;

        try
        {
            membership = await inviteService.RedeemInvite(Code, cancellationToken);
        }
        catch (UserFriendlyException exception)
        {
            logger.LogInformation("Invite not redeemed: {Reason}", exception.DeveloperMessage);
            Error = exception.UserMessage;
            return;
        }

        // Redeeming puts the repo in the shell's list, which navigates to it. The message is for the
        // case where it does not - a repo the user was already in, which the shell already had.
        // Clearing the box first: it is what wipes the message, and the message is the point.
        Code = "";
        JoinedRepoName = membership.Repo.Name;
    }


    partial void OnCodeChanged(string value)
    {
        JoinedRepoName = null;
        Error = null;
    }
}
