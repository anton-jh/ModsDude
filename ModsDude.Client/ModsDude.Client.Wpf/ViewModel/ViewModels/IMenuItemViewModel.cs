
using ModsDude.Client.Wpf.ViewModel.Pages;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

public interface IMenuItemViewModel
{
    public string Title { get; }
    public PageViewModel GetPage();
}
