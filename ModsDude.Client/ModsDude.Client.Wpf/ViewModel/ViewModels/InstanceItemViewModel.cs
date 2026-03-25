using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.ViewModel.Pages;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

public class InstanceItemViewModel(
    RepoModel repo,
    LocalInstance instance)
    : IMenuItemViewModel
{
    public string Title => instance.Name;

    public PageViewModel GetPage()
    {
        throw new NotImplementedException();
    }
}
