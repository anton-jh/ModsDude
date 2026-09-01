using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

/// <summary>
/// The way into somebody else's repo: paste the code they sent you.
/// </summary>
/// <remarks>
/// There is nothing to search and nobody to be found. A user is reachable only through a code they
/// were handed, which is what makes it safe for two people to be called the same thing - and what
/// stops anybody being added to a repo they never asked to be in.
/// </remarks>
public partial class JoinRepoPageViewModel : PageViewModel
{
    private readonly InviteService _inviteService;


    public JoinRepoPageViewModel(InviteService inviteService)
    {
        _inviteService = inviteService;
    }


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

    public bool HasJoined => JoinedRepoName is not null;

    public bool CanJoin => !string.IsNullOrWhiteSpace(Code);


    [RelayCommand(CanExecute = nameof(CanJoin))]
    public async Task Join(CancellationToken cancellationToken)
    {
        var membership = await _inviteService.RedeemInvite(Code, cancellationToken);

        // Redeeming puts the repo in the shell's list, which navigates to it. The message is for the
        // case where it does not - a repo the user was already in, which the shell already had.
        // Clearing the box first: it is what wipes the message, and the message is the point.
        Code = "";
        JoinedRepoName = membership.Repo.Name;
    }


    partial void OnCodeChanged(string value)
    {
        JoinedRepoName = null;
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public JoinRepoPageViewModel Create()
            => ActivatorUtilities.CreateInstance<JoinRepoPageViewModel>(serviceProvider);
    }
}
