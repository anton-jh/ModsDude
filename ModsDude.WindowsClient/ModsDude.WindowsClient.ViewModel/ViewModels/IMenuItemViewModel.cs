
using ModsDude.WindowsClient.ViewModel.Pages;

namespace ModsDude.WindowsClient.ViewModel.ViewModels;

public interface IMenuItemViewModel
{
    string Title { get; }
    PageViewModel GetPage();
}
