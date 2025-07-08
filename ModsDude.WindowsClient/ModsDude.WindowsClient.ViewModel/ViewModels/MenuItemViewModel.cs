using ModsDude.WindowsClient.ViewModel.Pages;

namespace ModsDude.WindowsClient.ViewModel.ViewModels;
public class MenuItemViewModel(
    string title,
    Func<PageViewModel> getPage)
    : IMenuItemViewModel
{
    public MenuItemViewModel(string title, PageViewModel page)
        : this(title, () => page)
    {
    }


    public string Title { get; } = title;

    public PageViewModel GetPage()
    {
        return getPage();
    }
}
