
using ModsDude.Client.Wpf.ViewModel.Pages;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

public interface IMenuItemViewModel
{
    string Title { get; }
    PageViewModel GetPage();
}
