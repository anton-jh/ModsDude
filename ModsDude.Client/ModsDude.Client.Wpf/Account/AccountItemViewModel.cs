using ModsDude.Client.Wpf.Shell.Navigation;
using ModsDude.Client.Wpf.Shell.Sidebar;

namespace ModsDude.Client.Wpf.Account;

/// <summary>
/// The account page's entry in the rail, drawn as the user's avatar and labelled with their name and tag.
/// </summary>
public sealed class AccountItemViewModel(AccountViewModel account, Func<PageViewModel> getPage)
    : MenuItemViewModel("Account", getPage, account, () => account.Description, nameof(AccountViewModel.Description))
{
    public AccountViewModel Account { get; } = account;
}
