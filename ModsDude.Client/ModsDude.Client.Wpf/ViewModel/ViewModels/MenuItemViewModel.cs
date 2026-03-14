using ModsDude.Client.Wpf.ViewModel.Pages;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;
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
